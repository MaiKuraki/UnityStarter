using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Build.Pipeline.Editor.Integrations.YooAsset3Core;
using static Build.Pipeline.Editor.Integrations.YooAsset3Core.YooAsset3PublicationConstants;

namespace Build.Pipeline.Editor.Integrations.YooAsset3
{
    internal sealed class YooAsset3PublicationTransaction : IDisposable
    {
        private readonly string projectRoot;
        private readonly string buildOutputRoot;
        private readonly string bundledFileRoot;
        private readonly string invocationId;
        private readonly string publicationId;
        private readonly string stateRelativePath;
        private readonly string stateRoot;
        private readonly string activeJournalPath;
        private readonly Journal journal;
        private readonly YooAsset3PackagePublication[] packages;
        private readonly YooAsset3PackageBuildPlan[] finalPlans;
        private bool prepared;
        private bool completed;
        private bool disposed;
        private bool sourceQualificationScopeActive;
        private string sourceQualificationResumePhase = string.Empty;

        private YooAsset3PublicationTransaction(
            string projectRoot,
            string buildOutputRoot,
            string bundledFileRoot,
            string invocationId,
            Journal journal,
            YooAsset3PackagePublication[] packages,
            YooAsset3PackageBuildPlan[] finalPlans)
        {
            this.projectRoot = projectRoot;
            this.buildOutputRoot = buildOutputRoot;
            this.bundledFileRoot = bundledFileRoot;
            this.invocationId = YooAsset3PublicationPaths.NormalizeInvocationId(invocationId);
            publicationId = YooAsset3PublicationPaths.GetPublicationId(this.invocationId);
            stateRelativePath = YooAsset3PublicationPaths.GetStateRelativePath(this.invocationId);
            stateRoot = YooAsset3PublicationPaths.GetStateRoot(projectRoot, this.invocationId);
            activeJournalPath = Path.Combine(stateRoot, ActiveJournalFileName);
            this.journal = journal;
            this.packages = packages;
            this.finalPlans = finalPlans;
        }

        public IReadOnlyList<YooAsset3PackagePublication> Packages => packages;
        // A bundled target only counts as a downstream input when it manages sibling
        // meta, i.e. it lives under Assets/StreamingAssets and will be swept into the
        // Player by Unity. Bundled targets elsewhere are not copied into the Player,
        // so they must not trigger downstream activation. This aligns with the
        // managesSiblingMeta filter in ActivateDownstreamInputs and the defensive
        // continue in HidePublicationArtifacts.
        internal bool HasDownstreamInputs => packages.Any(package =>
            package.BundledOperation != null && package.BundledOperation.managesSiblingMeta);
        internal string PublicationId => publicationId;
        internal string StateRelativePath => stateRelativePath;

        internal YooAsset3PackageBuildPlan GetFinalPlan(YooAsset3PackagePublication publication)
        {
            int index = Array.IndexOf(packages, publication);
            if (index < 0)
            {
                throw new InvalidOperationException(
                    "The YooAsset publication does not belong to this transaction.");
            }

            return finalPlans[index];
        }

        // Compatibility facades. The durable publication recovery logic and its path
        // helpers now live in the core assembly (YooAsset3PublicationRecovery and
        // YooAsset3PublicationPaths) so recovery still works when the YooAsset package
        // is uninstalled. These one-line delegations preserve the transaction's public
        // surface for the gated build adapter and integration tests.
        public static string GetProviderStateRoot(string projectRoot)
        {
            return YooAsset3PublicationPaths.GetProviderStateRoot(projectRoot);
        }

        public static string GetStateRoot(
            string projectRoot,
            string invocationId)
        {
            return YooAsset3PublicationPaths.GetStateRoot(projectRoot, invocationId);
        }

        internal static string GetStateRelativePath(string invocationId)
        {
            return YooAsset3PublicationPaths.GetStateRelativePath(invocationId);
        }

        internal static string GetPublicationId(string invocationId)
        {
            return YooAsset3PublicationPaths.GetPublicationId(invocationId);
        }

        public static void RecoverPending(string projectRoot, Action refreshAssets)
        {
            YooAsset3PublicationRecovery.RecoverPending(projectRoot, refreshAssets);
        }

        internal static void EnsureNoPendingRecovery(
            string projectRoot,
            string invocationId)
        {
            YooAsset3PublicationRecovery.EnsureNoPendingRecovery(projectRoot, invocationId);
        }

        public static YooAsset3PublicationTransaction Create(
            YooAsset3BuildPlan plan,
            string invocationId)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            string normalizedInvocationId = YooAsset3PublicationPaths.NormalizeInvocationId(invocationId);
            string transactionId = Guid.NewGuid().ToString("N");
            string stateRoot = GetStateRoot(
                plan.ProjectRoot,
                normalizedInvocationId);
            string workRoot = Path.GetFullPath(Path.Combine(stateRoot, "work", transactionId));
            var operations = new List<YooAsset3PublicationJournalOperation>(plan.Packages.Length * 2);
            var publications = new YooAsset3PackagePublication[plan.Packages.Length];

            for (int index = 0; index < plan.Packages.Length; index++)
            {
                YooAsset3PackageBuildPlan packagePlan = plan.Packages[index];
                string suffix = transactionId + "-" + index.ToString("D3", CultureInfo.InvariantCulture);
                YooAsset3PublicationJournalOperation outputOperation = CreateOperation(
                    plan.ProjectRoot,
                    YooAsset3PublicationOwnership.PackageOutputKind,
                    packagePlan.PackageName,
                    packagePlan.PackageVersion,
                    packagePlan.CryptographyAdapterId,
                    packagePlan.RuntimeDecryptContractId,
                    plan.BuildOutputRoot,
                    packagePlan.OutputPackageDirectory,
                    suffix);
                operations.Add(outputOperation);

                YooAsset3PublicationJournalOperation bundledOperation = null;
                string bundledWorkDirectory = string.Empty;
                if (packagePlan.Parameters.BundledCopyOption != YooAsset.Editor.EBundledCopyOption.None)
                {
                    bundledOperation = CreateOperation(
                        plan.ProjectRoot,
                        YooAsset3PublicationOwnership.BundledPackageKind,
                        packagePlan.PackageName,
                        packagePlan.PackageVersion,
                        packagePlan.CryptographyAdapterId,
                        packagePlan.RuntimeDecryptContractId,
                        plan.BundledFileRoot,
                        packagePlan.BundledPackageDirectory,
                        suffix);
                    operations.Add(bundledOperation);
                    bundledWorkDirectory = Path.GetFullPath(Path.Combine(
                        workRoot,
                        "bundled",
                        index.ToString("D3", CultureInfo.InvariantCulture)));
                }

                publications[index] = new YooAsset3PackagePublication(
                    outputOperation,
                    bundledOperation,
                    bundledWorkDirectory);
            }

            var journal = new Journal
            {
                documentType = JournalDocumentType,
                invocationId = normalizedInvocationId,
                transactionId = transactionId,
                phase = PreparedPhase,
                projectRoot = Path.GetFullPath(plan.ProjectRoot),
                buildOutputRoot = Path.GetFullPath(plan.BuildOutputRoot),
                bundledFileRoot = Path.GetFullPath(plan.BundledFileRoot),
                workRoot = workRoot,
                operations = operations.ToArray()
            };

            ValidateTransactionPathBudgets(journal, publications, plan.Packages);
            return new YooAsset3PublicationTransaction(
                journal.projectRoot,
                journal.buildOutputRoot,
                journal.bundledFileRoot,
                normalizedInvocationId,
                journal,
                publications,
                plan.Packages);
        }

        private static void ValidateTransactionPathBudgets(
            Journal value,
            YooAsset3PackagePublication[] packagePublications,
            YooAsset3PackageBuildPlan[] finalPlans)
        {
            YooAsset3PublicationRecovery.ValidateJournalPathBudgets(value);
            for (int index = 0; index < packagePublications.Length; index++)
            {
                YooAsset3PackagePublication publication = packagePublications[index];
                if (!string.IsNullOrEmpty(publication.BundledWorkDirectory))
                {
                    BuildPathPolicy.EnsureWin32MaxDirectoryPathBudget(
                        publication.BundledWorkDirectory,
                        $"YooAsset bundled work directory '{finalPlans[index].PackageName}'",
                        65);
                }
            }
        }

        public void Prepare(Action<string> checkpoint = null)
        {
            ThrowIfDisposed();
            if (prepared)
            {
                throw new InvalidOperationException("The YooAsset publication transaction is already prepared.");
            }

            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, stateRoot);
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, activeJournalPath);
            Directory.CreateDirectory(stateRoot);
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, stateRoot);
            YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, activeJournalPath);
            if (File.Exists(activeJournalPath))
            {
                throw new InvalidOperationException(
                    $"A pending YooAsset publication journal must be recovered before starting a new transaction: '{activeJournalPath}'.");
            }

            YooAsset3PublicationRecovery.EnsureNoDetachedState(stateRoot);
            foreach (YooAsset3PublicationJournalOperation operation in journal.operations)
            {
                YooAsset3PublicationRecovery.ValidateOperation(operation, projectRoot, buildOutputRoot, bundledFileRoot, journal.transactionId);
            }

            EnsureNoOrphanOperationDirectories(journal.operations);
            foreach (YooAsset3PublicationJournalOperation operation in journal.operations)
            {
                CaptureOriginalPublication(operation);
            }

            YooAsset3PublicationRecovery.WriteJournal(journal, activeJournalPath, createNew: true);
            prepared = true;
            checkpoint?.Invoke("Prepared");

            foreach (YooAsset3PackagePublication package in packages)
            {
                if (package.BundledOperation == null || !RequiresBundledSeed(GetFinalPlan(package).Profile.bundledCopyOption))
                {
                    continue;
                }

                if (Directory.Exists(package.BundledOperation.target))
                {
                    YooAsset3PublicationRecovery.CopyDirectorySafely(
                        projectRoot,
                        package.BundledOperation.target,
                        package.BundledWorkDirectory,
                        package.BundledOperation.approvedRoot,
                        journal.workRoot);
                }
            }
        }

        public YooAsset3PackageBuildPlan CreateExecutionPlan(
            AssetContentBuildRequest request,
            YooAsset3PackagePublication publication)
        {
            ThrowIfDisposed();
            if (!prepared)
            {
                throw new InvalidOperationException("Prepare the YooAsset publication transaction before creating execution plans.");
            }

            YooAsset3PackageBuildPlan finalPlan = GetFinalPlan(publication);
            YooAsset3PackageBuildPlan executionPlan = YooAsset3BuildParameterFactory.Create(
                request,
                finalPlan.Profile,
                buildOutputRoot,
                bundledFileRoot,
                finalPlan.BundledCopyParams,
                publication.OutputOperation.stage,
                publication.BundledOperation == null
                    ? Path.Combine(journal.workRoot, "unused-bundled", finalPlan.PackageName)
                    : publication.BundledWorkDirectory);
            if (!string.Equals(
                    executionPlan.CryptographyAdapterId,
                    finalPlan.CryptographyAdapterId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    executionPlan.RuntimeDecryptContractId,
                    finalPlan.RuntimeDecryptContractId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"YooAsset cryptography identity changed between preflight and execution for package '{finalPlan.PackageName}'.");
            }

            return executionPlan;
        }

        public void PrepareReadyDirectories()
        {
            ThrowIfDisposed();
            foreach (YooAsset3PackagePublication package in packages)
            {
                YooAsset3PublicationJournalOperation bundledOperation = package.BundledOperation;
                if (bundledOperation == null)
                {
                    continue;
                }

                if (!Directory.Exists(package.BundledWorkDirectory))
                {
                    throw new DirectoryNotFoundException(
                        $"YooAsset did not produce its staged bundled package directory: '{package.BundledWorkDirectory}'.");
                }

                YooAsset3PublicationRecovery.EnsureOperationCandidateAbsent(bundledOperation);
                YooAsset3PublicationRecovery.CopyDirectorySafely(
                    projectRoot,
                    package.BundledWorkDirectory,
                    bundledOperation.stage,
                    journal.workRoot,
                    bundledOperation.approvedRoot);
            }
        }

        public void SealReadyDirectories(Action<string> checkpoint = null)
        {
            ThrowIfDisposed();
            if (!prepared)
            {
                throw new InvalidOperationException("Prepare the YooAsset publication transaction before sealing its stages.");
            }

            foreach (YooAsset3PublicationJournalOperation operation in journal.operations)
            {
                YooAsset3PublicationOwnership.PublicationSnapshot sealedStage = YooAsset3PublicationOwnership.Seal(
                    projectRoot,
                    operation.stage,
                    operation.kind,
                    operation.packageName,
                    operation.packageVersion,
                    operation.cryptographyAdapterId,
                    operation.runtimeDecryptContractId,
                    journal.transactionId);
                operation.installedContentIdentity = sealedStage.ContentIdentity;
                operation.installedEntryCount = sealedStage.EntryCount;
            }

            YooAsset3PublicationRecovery.WriteJournal(journal, activeJournalPath, createNew: false);
            checkpoint?.Invoke("Sealed");
        }

        internal void Publish(
            Action validatePublishedState,
            Action refreshAssets,
            Action<string> checkpoint = null)
        {
            ThrowIfDisposed();
            if (!prepared)
            {
                throw new InvalidOperationException("Prepare the YooAsset publication transaction before publishing it.");
            }

            try
            {
                if (!string.Equals(journal.phase, PreparedPhase, StringComparison.Ordinal)
                    && !string.Equals(journal.phase, DownstreamActivePhase, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"YooAsset publication cannot publish terminal outputs from phase '{journal.phase}'.");
                }

                YooAsset3PublicationJournalOperation[] pending = journal.operations
                    .Where(operation => string.Equals(
                        operation.state,
                        PreparedState,
                        StringComparison.Ordinal))
                    .ToArray();
                if (pending.Length == 0)
                {
                    throw new InvalidOperationException(
                        "YooAsset publication has no pending terminal output operations.");
                }

                ValidateReadyToCommit(pending);
                journal.phase = CommittingPhase;
                YooAsset3PublicationRecovery.WriteJournal(journal, activeJournalPath, createNew: false);
                foreach (YooAsset3PublicationJournalOperation operation in pending)
                {
                    CommitOperation(operation, checkpoint);
                }

                checkpoint?.Invoke("PreValidatePublishedState");
                validatePublishedState?.Invoke();
                checkpoint?.Invoke("PostValidatePublishedState");
                YooAsset3PublicationRecovery.ValidatePreRefreshCommittedPublications(journal);
                journal.phase = AwaitingDecisionPhase;
                YooAsset3PublicationRecovery.WriteJournal(journal, activeJournalPath, createNew: false);
            }
            catch (YooAsset3SimulatedTerminationException)
            {
                // A simulated crash leaves the durable journal exactly where the
                // checkpoint fired; no rollback runs because the process is "gone".
                throw;
            }
            catch (Exception publicationException)
            {
                try
                {
                    YooAsset3PublicationRecovery.Rollback(journal, activeJournalPath, refreshAssets, checkpoint);
                    completed = true;
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        "YooAsset publication failed and rollback did not complete. The durable journal was retained for recovery.",
                        publicationException,
                        rollbackException);
                }

                throw;
            }
        }

        internal void ActivateDownstreamInputs(Action refreshAssets, Action<string> checkpoint = null)
        {
            ThrowIfDisposed();
            if (!HasDownstreamInputs)
            {
                return;
            }

            if (!prepared || !string.Equals(journal.phase, PreparedPhase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "YooAsset bundled inputs can only be activated from the prepared phase.");
            }

            if (refreshAssets == null)
            {
                throw new ArgumentNullException(nameof(refreshAssets));
            }

            YooAsset3PublicationJournalOperation[] bundled = journal.operations
                .Where(operation => operation.managesSiblingMeta)
                .ToArray();
            try
            {
                ValidateReadyToCommit(bundled);
                journal.phase = CommittingPhase;
                YooAsset3PublicationRecovery.WriteJournal(journal, activeJournalPath, createNew: false);
                foreach (YooAsset3PublicationJournalOperation operation in bundled)
                {
                    CommitOperation(operation, checkpoint);
                }

                YooAsset3PublicationRecovery.ValidateDownstreamInputs(journal, afterRefresh: false);
                journal.phase = ActivationRefreshPendingPhase;
                YooAsset3PublicationRecovery.WriteJournal(journal, activeJournalPath, createNew: false);
                checkpoint?.Invoke("PreRefresh");
                refreshAssets();
                checkpoint?.Invoke("PostRefresh");
                YooAsset3PublicationRecovery.CaptureInstalledSiblingMetas(journal, recoveryCandidates: null);
                journal.phase = DownstreamActivePhase;
                YooAsset3PublicationRecovery.WriteJournal(journal, activeJournalPath, createNew: false);
            }
            catch (YooAsset3SimulatedTerminationException)
            {
                // A simulated crash leaves the durable journal exactly where the
                // checkpoint fired; no cleanup runs because the process is "gone".
                throw;
            }
            catch
            {
                YooAsset3PublicationRecovery.CaptureActivatedSiblingMetasForRollback(journal);
                if (bundled.All(operation => string.Equals(
                    operation.state,
                    InstalledState,
                    StringComparison.Ordinal)))
                {
                    journal.phase = DownstreamActivePhase;
                }

                YooAsset3PublicationRecovery.WriteJournal(journal, activeJournalPath, createNew: false);
                throw;
            }
        }

        internal void ValidateActivatedInputs()
        {
            ThrowIfDisposed();
            if (!HasDownstreamInputs)
            {
                return;
            }

            if (!string.Equals(journal.phase, DownstreamActivePhase, StringComparison.Ordinal)
                && !string.Equals(journal.phase, AwaitingDecisionPhase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "YooAsset bundled inputs are not active at the terminal decision boundary.");
            }

            YooAsset3PublicationRecovery.ValidateDownstreamInputs(journal, afterRefresh: true);
        }

        internal IDisposable SuspendForSourceQualification()
        {
            ThrowIfDisposed();
            if (!HasDownstreamInputs)
            {
                return SourceQualificationScope.Empty;
            }

            if (sourceQualificationScopeActive)
            {
                throw new InvalidOperationException(
                    "YooAsset bundled inputs are already suspended for source qualification.");
            }

            bool downstreamActive = string.Equals(
                journal.phase,
                DownstreamActivePhase,
                StringComparison.Ordinal);
            bool preparedOnly = string.Equals(
                journal.phase,
                PreparedPhase,
                StringComparison.Ordinal);
            if (!prepared || (!downstreamActive && !preparedOnly))
            {
                throw new InvalidOperationException(
                    $"YooAsset bundled inputs can only be suspended for source qualification from phase '{PreparedPhase}' or '{DownstreamActivePhase}', " +
                    $"but the transaction is in phase '{journal.phase}'.");
            }

            if (downstreamActive)
            {
                YooAsset3PublicationRecovery.ValidateDownstreamInputs(journal, afterRefresh: true);
            }
            else
            {
                YooAsset3PublicationRecovery.ValidatePreparedForSourceQualification(journal);
            }

            sourceQualificationResumePhase = journal.phase;
            journal.phase = SourceQualificationSuspendingPhase;
            YooAsset3PublicationRecovery.WriteJournal(journal, activeJournalPath, createNew: false);

            try
            {
                string suspensionRoot = YooAsset3PublicationRecovery.GetSourceQualificationRoot(journal);
                YooAsset3PublicationRecovery.EnsureSourceQualificationRootCanBeCreated(journal, suspensionRoot);
                Directory.CreateDirectory(suspensionRoot);
                YooAsset3BuildSafety.ValidateNoPathRedirection(projectRoot, suspensionRoot);

                for (int index = journal.operations.Length - 1; index >= 0; index--)
                {
                    YooAsset3PublicationJournalOperation operation = journal.operations[index];
                    if (!operation.managesSiblingMeta)
                    {
                        continue;
                    }

                    YooAsset3PublicationRecovery.SuspendBundledOperation(
                        journal,
                        operation,
                        index,
                        downstreamActive);
                }

                YooAsset3PublicationRecovery.ValidateSourceQualificationSuspended(
                    journal,
                    downstreamActive);
                journal.phase = SourceQualificationSuspendedPhase;
                YooAsset3PublicationRecovery.WriteJournal(journal, activeJournalPath, createNew: false);
                sourceQualificationScopeActive = true;
                return new SourceQualificationScope(this);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "YooAsset could not restore the exact pre-build bundled source tree for source qualification. " +
                    "The durable publication journal was retained so normal build rollback or workspace recovery can restore the original state.",
                    exception);
            }
        }

        private void ResumeAfterSourceQualification()
        {
            ThrowIfDisposed();
            if (!sourceQualificationScopeActive)
            {
                throw new InvalidOperationException(
                    "YooAsset source qualification suspension is not active.");
            }

            if (!string.Equals(journal.phase, SourceQualificationSuspendedPhase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"YooAsset source qualification suspension cannot resume from phase '{journal.phase}'.");
            }

            bool downstreamActive = string.Equals(
                sourceQualificationResumePhase,
                DownstreamActivePhase,
                StringComparison.Ordinal);
            bool preparedOnly = string.Equals(
                sourceQualificationResumePhase,
                PreparedPhase,
                StringComparison.Ordinal);
            if (!downstreamActive && !preparedOnly)
            {
                throw new InvalidOperationException(
                    "YooAsset source qualification suspension lost its resume phase.");
            }

            YooAsset3PublicationRecovery.ValidateSourceQualificationSuspended(
                journal,
                downstreamActive);
            journal.phase = SourceQualificationResumingPhase;
            YooAsset3PublicationRecovery.WriteJournal(journal, activeJournalPath, createNew: false);

            try
            {
                for (int index = 0; index < journal.operations.Length; index++)
                {
                    YooAsset3PublicationJournalOperation operation = journal.operations[index];
                    if (!operation.managesSiblingMeta)
                    {
                        continue;
                    }

                    YooAsset3PublicationRecovery.ResumeBundledOperation(
                        journal,
                        operation,
                        index,
                        downstreamActive);
                }

                if (downstreamActive)
                {
                    YooAsset3PublicationRecovery.ValidateDownstreamInputs(journal, afterRefresh: true);
                }
                else
                {
                    YooAsset3PublicationRecovery.ValidatePreparedForSourceQualification(journal);
                }

                YooAsset3PublicationRecovery.DeleteSourceQualificationRoot(journal);
                journal.phase = sourceQualificationResumePhase;
                YooAsset3PublicationRecovery.WriteJournal(journal, activeJournalPath, createNew: false);
                sourceQualificationScopeActive = false;
                sourceQualificationResumePhase = string.Empty;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "YooAsset could not reactivate its bundled downstream inputs after source qualification. " +
                    "The durable publication journal was retained so normal build rollback or workspace recovery can restore the original state.",
                    exception);
            }
        }

        internal void Complete(Action refreshAssets, Action<string> checkpoint = null)
        {
            ThrowIfDisposed();
            if (!prepared || !string.Equals(journal.phase, AwaitingDecisionPhase, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "YooAsset publication has not reached the terminal decision barrier.");
            }

            BuildPublicationDecision decision = BuildPublicationBarrier.GetDecision(
                projectRoot,
                PublicationId,
                StateRelativePath);
            if (decision != BuildPublicationDecision.Commit)
            {
                throw new InvalidOperationException(
                    "YooAsset publication completion requires an explicit durable Commit decision from the terminal barrier.");
            }

            // Complete is invoked only after the shared barrier has persisted its
            // commit decision. From this point disposal must preserve evidence for
            // explicit recovery instead of attempting a contradictory rollback.
            completed = true;
            try
            {
                YooAsset3PublicationRecovery.ValidatePreRefreshCommittedPublications(journal);
                journal.phase = RefreshPendingPhase;
                YooAsset3PublicationRecovery.WriteJournal(journal, activeJournalPath, createNew: false);
                YooAsset3PublicationRecovery.CompletePendingRefresh(journal, activeJournalPath, refreshAssets, checkpoint);
            }
            catch (YooAsset3SimulatedTerminationException)
            {
                throw;
            }
            catch (YooAsset3CommittedPublicationException)
            {
                throw;
            }
            catch (Exception completionException)
            {
                throw new YooAsset3CommittedPublicationException(
                    "YooAsset publication was selected by the terminal commit barrier, but durable refresh finalization did not complete. " +
                    "The journal and backups were retained for explicit recovery.",
                    activeJournalPath,
                    completionException);
            }
        }

        public void Abort(Action refreshAssets)
        {
            ThrowIfDisposed();
            if (completed)
            {
                return;
            }

            if (prepared && File.Exists(activeJournalPath))
            {
                if (BuildPublicationBarrier.GetDecision(
                        projectRoot,
                        PublicationId,
                        StateRelativePath)
                    == BuildPublicationDecision.Commit)
                {
                    completed = true;
                    return;
                }

                YooAsset3PublicationRecovery.Rollback(journal, activeJournalPath, refreshAssets);
            }

            completed = true;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            if (!completed)
            {
                Abort(refreshAssets: null);
            }

            disposed = true;
        }

        private static YooAsset3PublicationJournalOperation CreateOperation(
            string projectRoot,
            string kind,
            string packageName,
            string packageVersion,
            string cryptographyAdapterId,
            string runtimeDecryptContractId,
            string approvedRoot,
            string target,
            string suffix)
        {
            string normalizedTarget = Path.GetFullPath(target);
            string parent = Path.GetDirectoryName(normalizedTarget);
            if (string.IsNullOrEmpty(parent))
            {
                throw new InvalidOperationException($"Publication target does not have a parent directory: '{normalizedTarget}'.");
            }

            string stage = Path.Combine(parent, StagePrefix + suffix);
            string backup = Path.Combine(parent, BackupPrefix + suffix);
            string streamingAssetsRoot = Path.GetFullPath(Path.Combine(projectRoot, "Assets", "StreamingAssets"));
            bool managesSiblingMeta =
                string.Equals(kind, YooAsset3PublicationOwnership.BundledPackageKind, StringComparison.Ordinal) &&
                (YooAsset3BuildSafety.PathsEqual(streamingAssetsRoot, normalizedTarget) ||
                 YooAsset3BuildSafety.IsStrictDescendant(streamingAssetsRoot, normalizedTarget));
            return new YooAsset3PublicationJournalOperation
            {
                kind = kind,
                packageName = packageName,
                packageVersion = packageVersion,
                cryptographyAdapterId = cryptographyAdapterId,
                runtimeDecryptContractId = runtimeDecryptContractId,
                approvedRoot = Path.GetFullPath(approvedRoot),
                target = normalizedTarget,
                stage = stage,
                backup = backup,
                managesSiblingMeta = managesSiblingMeta,
                targetMeta = managesSiblingMeta ? normalizedTarget + ".meta" : string.Empty,
                protectedMeta = managesSiblingMeta ? backup + ".root-meta" : string.Empty,
                state = PreparedState
            };
        }

        private void CaptureOriginalPublication(YooAsset3PublicationJournalOperation operation)
        {
            YooAsset3PublicationRecovery.ValidateOperation(operation, projectRoot, buildOutputRoot, bundledFileRoot, journal.transactionId);
            YooAsset3PublicationOwnership.PublicationSnapshot original = YooAsset3PublicationOwnership.CaptureExisting(
                projectRoot,
                operation.target,
                operation.kind,
                operation.packageName);
            operation.targetInitiallyExisted = original.Exists;
            operation.originalWasOwned = original.Owned;
            operation.originalTransactionId = original.TransactionId;
            operation.originalPackageVersion = original.PackageVersion;
            operation.originalCryptographyAdapterId = original.CryptographyAdapterId;
            operation.originalRuntimeDecryptContractId = original.RuntimeDecryptContractId;
            operation.originalContentIdentity = original.ContentIdentity;
            operation.originalEntryCount = original.EntryCount;
            if (!operation.managesSiblingMeta)
            {
                return;
            }

            MetaFileSnapshot originalMeta = YooAsset3PublicationRecovery.CaptureMetaFile(projectRoot, operation.targetMeta);
            if (original.Exists != originalMeta.Exists)
            {
                throw new InvalidOperationException(
                    $"Bundled publication directory and its sibling meta file must either both exist or both be absent: " +
                    $"'{operation.target}', '{operation.targetMeta}'.");
            }

            operation.originalMetaExisted = originalMeta.Exists;
            operation.originalMetaLength = originalMeta.Length;
            operation.originalMetaSha256 = originalMeta.Sha256;
        }

        private void ValidateReadyToCommit(
            IReadOnlyList<YooAsset3PublicationJournalOperation> operations)
        {
            if (operations == null || operations.Count == 0)
            {
                throw new InvalidOperationException(
                    "YooAsset publication has no operations to commit.");
            }

            foreach (YooAsset3PublicationJournalOperation operation in operations)
            {
                if (operation == null ||
                    !string.Equals(operation.state, PreparedState, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "YooAsset publication can only commit prepared operations.");
                }

                YooAsset3PublicationRecovery.ValidateDirectoryMovePathBudgets(
                    operation.stage,
                    operation.target,
                    $"YooAsset published artifact '{operation.packageName}'");
                if (operation.targetInitiallyExisted)
                {
                    YooAsset3PublicationRecovery.ValidateDirectoryMovePathBudgets(
                        operation.target,
                        operation.backup,
                        $"YooAsset backup artifact '{operation.packageName}'");
                }

                YooAsset3PublicationRecovery.ValidateOriginalPublicationAt(operation, operation.target, projectRoot);
                YooAsset3PublicationRecovery.ValidateInstalledPublicationAt(operation, operation.stage, projectRoot, journal.transactionId);
                if (Directory.Exists(operation.backup) || File.Exists(operation.backup))
                {
                    throw new InvalidOperationException($"Publication backup path is not empty: '{operation.backup}'.");
                }

                if (operation.managesSiblingMeta &&
                    (File.Exists(operation.protectedMeta) || Directory.Exists(operation.protectedMeta)))
                {
                    throw new InvalidOperationException(
                        $"Publication protected meta path is not empty: '{operation.protectedMeta}'.");
                }
            }
        }

        private void CommitOperation(YooAsset3PublicationJournalOperation operation, Action<string> checkpoint = null)
        {
            YooAsset3PublicationRecovery.ValidateOperation(operation, projectRoot, buildOutputRoot, bundledFileRoot, journal.transactionId);
            YooAsset3PublicationRecovery.ValidateInstalledPublicationAt(operation, operation.stage, projectRoot, journal.transactionId);
            YooAsset3PublicationRecovery.ValidateOriginalPublicationAt(operation, operation.target, projectRoot);

            if (Directory.Exists(operation.backup) || File.Exists(operation.backup))
            {
                throw new InvalidOperationException($"Publication backup path is not empty: '{operation.backup}'.");
            }

            operation.state = BackupPendingState;
            YooAsset3PublicationRecovery.WriteJournal(journal, activeJournalPath, createNew: false);
            checkpoint?.Invoke($"BackupPending:{operation.packageName}");
            if (operation.targetInitiallyExisted)
            {
                ProtectOriginalSiblingMeta(projectRoot, operation);
                Directory.Move(operation.target, operation.backup);
                YooAsset3PublicationRecovery.ValidateOriginalPublicationAt(operation, operation.backup, projectRoot);
            }

            operation.state = BackedUpState;
            YooAsset3PublicationRecovery.WriteJournal(journal, activeJournalPath, createNew: false);
            checkpoint?.Invoke($"BackedUp:{operation.packageName}");
            if (Directory.Exists(operation.target) || File.Exists(operation.target))
            {
                throw new InvalidOperationException(
                    $"Publication target appeared while committing package '{operation.packageName}': '{operation.target}'.");
            }

            YooAsset3PublicationRecovery.ValidateInstalledPublicationAt(operation, operation.stage, projectRoot, journal.transactionId);
            Directory.Move(operation.stage, operation.target);
            YooAsset3PublicationRecovery.ValidateInstalledPublicationAt(operation, operation.target, projectRoot, journal.transactionId);
            YooAsset3PublicationRecovery.ValidatePreRefreshSiblingMeta(projectRoot, operation, allowMissingOriginalMeta: false);
            operation.state = InstalledState;
            YooAsset3PublicationRecovery.WriteJournal(journal, activeJournalPath, createNew: false);
            checkpoint?.Invoke($"Installed:{operation.packageName}");
        }

        private static void ProtectOriginalSiblingMeta(
            string projectRoot,
            YooAsset3PublicationJournalOperation operation)
        {
            if (!operation.managesSiblingMeta)
            {
                return;
            }

            YooAsset3PublicationRecovery.ValidateMetaFile(
                projectRoot,
                operation.targetMeta,
                operation.originalMetaExisted,
                operation.originalMetaLength,
                operation.originalMetaSha256,
                "original bundled publication meta");
            if (!operation.originalMetaExisted)
            {
                return;
            }

            if (File.Exists(operation.protectedMeta) || Directory.Exists(operation.protectedMeta))
            {
                throw new InvalidOperationException(
                    $"Protected bundled publication meta path is not empty: '{operation.protectedMeta}'.");
            }

            YooAsset3PublicationRecovery.CopyMetaFileDurably(operation.targetMeta, operation.protectedMeta);
            YooAsset3PublicationRecovery.ValidateMetaFile(
                projectRoot,
                operation.protectedMeta,
                true,
                operation.originalMetaLength,
                operation.originalMetaSha256,
                "protected bundled publication meta");
        }

        private static bool RequiresBundledSeed(YooAssetBundledCopyOption option)
        {
            return option == YooAssetBundledCopyOption.OnlyCopyAll ||
                   option == YooAssetBundledCopyOption.OnlyCopyByTags;
        }

        private static void EnsureNoOrphanOperationDirectories(
            IEnumerable<YooAsset3PublicationJournalOperation> operations)
        {
            foreach (string parent in operations
                         .Select(operation => Path.GetDirectoryName(operation.target))
                         .Where(parent => !string.IsNullOrEmpty(parent))
                         .Distinct(YooAsset3BuildSafety.FileSystemPathComparer))
            {
                if (!Directory.Exists(parent))
                {
                    continue;
                }

                foreach (string entry in Directory.EnumerateFileSystemEntries(parent, "*", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(entry);
                    if (name.StartsWith(StagePrefix, StringComparison.Ordinal) ||
                        name.StartsWith(BackupPrefix, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"Detached YooAsset transaction state requires manual inspection: '{entry}'.");
                    }
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(YooAsset3PublicationTransaction));
            }
        }

        private sealed class SourceQualificationScope : IDisposable
        {
            internal static readonly IDisposable Empty = new SourceQualificationScope(null);
            private YooAsset3PublicationTransaction owner;

            internal SourceQualificationScope(YooAsset3PublicationTransaction owner)
            {
                this.owner = owner;
            }

            public void Dispose()
            {
                YooAsset3PublicationTransaction current = owner;
                owner = null;
                current?.ResumeAfterSourceQualification();
            }
        }
    }
}
