using System;
using System.Collections.Generic;

namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Drives a batch of resource loads through an <see cref="IResourceProvider"/> using a poll-based model that
    /// carries no engine or third-party async type. Call <see cref="Begin"/>, then <see cref="Update"/> once per
    /// frame (or in a loop) until <see cref="IsDone"/>. The runner retains successful handles so the referenced
    /// resources stay resident; call <see cref="ReleaseAll"/> when the caller no longer needs them.
    ///
    /// Supports concurrency throttling, progress reporting, cancellation, and per-reference failure policy. All
    /// buffers are reused across batches, so repeated preloads on a pooled runner do not allocate after warm-up.
    /// </summary>
    public sealed class PreloadRunner
    {
        private readonly IResourceProvider _provider;
        private readonly IChoreographyDiagnostics _diagnostics;

        private readonly List<ChoreographyResourceReference> _references = new List<ChoreographyResourceReference>(16);
        private readonly List<IChoreographyResourceHandle> _active = new List<IChoreographyResourceHandle>(16);
        private readonly List<IChoreographyResourceHandle> _completed = new List<IChoreographyResourceHandle>(16);
        private readonly List<ChoreographyResourceReference> _failed = new List<ChoreographyResourceReference>(4);

        private PreloadOptions _options;
        private PreloadStatus _status = PreloadStatus.Idle;
        private int _nextToStart;
        private int _succeededCount;
        private float _progress;

        /// <summary>Raised whenever <see cref="Progress"/> changes during <see cref="Update"/>.</summary>
        public event Action<float> ProgressChanged;

        /// <summary>Raised once when the batch finishes (completed, failed, or cancelled).</summary>
        public event Action<PreloadResult> Completed;

        public PreloadRunner(IResourceProvider provider, IChoreographyDiagnostics diagnostics = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _diagnostics = diagnostics ?? NullChoreographyDiagnostics.Instance;
        }

        public PreloadStatus Status => _status;

        public bool IsDone => _status == PreloadStatus.Completed || _status == PreloadStatus.Failed || _status == PreloadStatus.Cancelled;

        public float Progress => _progress;

        public int TotalCount => _references.Count;

        /// <summary>
        /// Begins loading the supplied references. Any handles retained by a previous batch are released first.
        /// Passing an empty list completes immediately with <see cref="PreloadStatus.Completed"/>.
        /// </summary>
        public void Begin(IReadOnlyList<ChoreographyResourceReference> references, PreloadOptions options)
        {
            if (references == null)
            {
                throw new ArgumentNullException(nameof(references));
            }

            ReleaseAll();

            _options = options;
            _references.Clear();
            for (int i = 0; i < references.Count; i++)
            {
                _references.Add(references[i]);
            }

            _failed.Clear();
            _nextToStart = 0;
            _succeededCount = 0;
            _progress = 0f;

            if (_references.Count == 0)
            {
                _progress = 1f;
                _status = PreloadStatus.Completed;
                RaiseCompleted();
                return;
            }

            _status = PreloadStatus.Loading;
            int startBudget = _options.MaxConcurrent > 0 ? _options.MaxConcurrent : _references.Count;
            StartUpTo(startBudget);
        }

        /// <summary>Polls in-flight handles, advances progress, promotes pending loads, and detects completion.</summary>
        public void Update()
        {
            if (_status != PreloadStatus.Loading)
            {
                return;
            }

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                IChoreographyResourceHandle handle = _active[i];
                if (!handle.IsDone)
                {
                    continue;
                }

                _active.RemoveAt(i);
                if (handle.Succeeded)
                {
                    _completed.Add(handle);
                    _succeededCount++;
                }
                else
                {
                    _failed.Add(handle.Reference);
                    handle.Release();
                    if (_diagnostics.IsEnabled(ChoreographyLogLevel.Warning))
                    {
                        _diagnostics.Log(ChoreographyLogLevel.Warning, "Choreography",
                            "Preload failed for '" + handle.Reference.Address + "': " + (handle.Error ?? "unknown error"));
                    }

                    if (_options.FailurePolicy == PreloadFailurePolicy.Abort)
                    {
                        Abort();
                        return;
                    }
                }
            }

            int startBudget = _options.MaxConcurrent > 0 ? _options.MaxConcurrent - _active.Count : int.MaxValue;
            if (startBudget > 0)
            {
                StartUpTo(startBudget);
            }

            UpdateProgress();

            if (_active.Count == 0 && _nextToStart >= _references.Count)
            {
                _status = PreloadStatus.Completed;
                _progress = 1f;
                ProgressChanged?.Invoke(_progress);
                RaiseCompleted();
            }
        }

        /// <summary>Cancels an in-flight batch, releasing every in-flight and completed handle.</summary>
        public void Cancel()
        {
            if (_status != PreloadStatus.Loading)
            {
                return;
            }

            ReleaseHandles();
            _status = PreloadStatus.Cancelled;
            RaiseCompleted();
        }

        /// <summary>Releases all retained handles from the current/last batch. Safe to call multiple times.</summary>
        public void ReleaseAll()
        {
            ReleaseHandles();
            if (_status == PreloadStatus.Loading)
            {
                _status = PreloadStatus.Cancelled;
            }
        }

        private void StartUpTo(int budget)
        {
            int started = 0;
            while (started < budget && _nextToStart < _references.Count)
            {
                ChoreographyResourceReference reference = _references[_nextToStart];
                _nextToStart++;

                if (!reference.IsValid)
                {
                    _failed.Add(reference);
                    if (_options.FailurePolicy == PreloadFailurePolicy.Abort)
                    {
                        Abort();
                        return;
                    }
                    continue;
                }

                IChoreographyResourceHandle handle = _provider.Load(in reference);
                _active.Add(handle);
                started++;
            }
        }

        private void UpdateProgress()
        {
            int total = _references.Count;
            if (total == 0)
            {
                return;
            }

            float accumulated = _succeededCount + _failed.Count;
            for (int i = 0; i < _active.Count; i++)
            {
                accumulated += _active[i].Progress;
            }

            float next = accumulated / total;
            if (next > 1f)
            {
                next = 1f;
            }

            if (next != _progress)
            {
                _progress = next;
                ProgressChanged?.Invoke(_progress);
            }
        }

        private void Abort()
        {
            ReleaseHandles();
            _status = PreloadStatus.Failed;
            RaiseCompleted();
        }

        private void ReleaseHandles()
        {
            for (int i = 0; i < _active.Count; i++)
            {
                _active[i].Release();
            }
            _active.Clear();

            for (int i = 0; i < _completed.Count; i++)
            {
                _completed[i].Release();
            }
            _completed.Clear();
        }

        private void RaiseCompleted()
        {
            Completed?.Invoke(new PreloadResult(_status, _references.Count, _succeededCount, _failed.Count, _failed));
        }
    }
}
