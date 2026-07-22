using System;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.Persistence
{
    /// <summary>
    /// Binds one storage location to one codec profile. The store owns no application state.
    /// </summary>
    public sealed class PersistenceStore<T>
    {
        private readonly IPersistenceStorage _storage;
        private readonly PersistenceProfile<T> _profile;
        private int _operationActive;

        public PersistenceStore(
            IPersistenceStorage storage,
            PersistenceProfile<T> profile)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        public string Location => _storage.Location;

        public PersistenceProfile<T> Profile => _profile;

        public Task<PersistenceLoadResult<T>> LoadAsync(
            int maximumSupportedContentVersion,
            CancellationToken cancellationToken = default)
        {
            if (maximumSupportedContentVersion < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSupportedContentVersion));
            }

            BeginOperation();
            try
            {
                return LoadCoreAsync(maximumSupportedContentVersion, cancellationToken);
            }
            catch
            {
                EndOperation();
                throw;
            }
        }

        public Task<PersistenceOperationResult> SaveAsync(
            in T value,
            int contentVersion,
            CancellationToken cancellationToken = default)
        {
            if (contentVersion < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(contentVersion));
            }

            BeginOperation();
            byte[] record = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                PersistenceLimits limits = _profile.Limits;
                var context = new PersistenceWriteContext(
                    contentVersion,
                    limits,
                    cancellationToken);
                using (var writer = new BoundedByteBufferWriter(
                           limits.InitialBufferBytes,
                           limits.MaximumPayloadBytes))
                {
                    _profile.Codec.Serialize(in value, writer, in context);
                    cancellationToken.ThrowIfCancellationRequested();
                    record = PersistenceRecordV1.Encode(
                        writer.WrittenSpan,
                        contentVersion,
                        _profile.CodecId,
                        limits);
                }

                cancellationToken.ThrowIfCancellationRequested();
                return SaveCoreAsync(record, cancellationToken);
            }
            catch (OperationCanceledException exception)
                when (cancellationToken.IsCancellationRequested)
            {
                Clear(record);
                EndOperation();
                return Task.FromResult(PersistenceOperationResult.Failure(
                    PersistenceErrorCode.Cancelled,
                    exception));
            }
            catch (PersistencePayloadBudgetExceededException exception)
            {
                Clear(record);
                EndOperation();
                return Task.FromResult(PersistenceOperationResult.Failure(
                    PersistenceErrorCode.PayloadTooLarge,
                    exception));
            }
            catch (Exception exception) when (PersistenceExceptionPolicy.IsRecoverable(exception))
            {
                Clear(record);
                EndOperation();
                return Task.FromResult(PersistenceOperationResult.Failure(
                    PersistenceErrorCode.SerializationFailed,
                    exception));
            }
            catch
            {
                Clear(record);
                EndOperation();
                throw;
            }
        }

        public Task<PersistenceOperationResult> DeleteAsync(
            CancellationToken cancellationToken = default)
        {
            BeginOperation();
            try
            {
                return DeleteCoreAsync(cancellationToken);
            }
            catch
            {
                EndOperation();
                throw;
            }
        }

        private async Task<PersistenceLoadResult<T>> LoadCoreAsync(
            int maximumSupportedContentVersion,
            CancellationToken cancellationToken)
        {
            byte[] record = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                PersistenceStorageReadResult storageResult = await _storage.ReadAsync(
                    _profile.Limits.MaximumRecordBytes,
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (storageResult.IsMissing)
                {
                    return PersistenceLoadResult<T>.Missing();
                }

                record = storageResult.Content;
                if (record == null)
                {
                    return PersistenceLoadResult<T>.Failure(
                        PersistenceErrorCode.ReadFailed,
                        new InvalidOperationException(
                            "The storage returned a found result without transferring a buffer."));
                }

                if (record.Length > _profile.Limits.MaximumRecordBytes)
                {
                    return PersistenceLoadResult<T>.Failure(
                        PersistenceErrorCode.PayloadTooLarge);
                }

                cancellationToken.ThrowIfCancellationRequested();
                PersistenceRecordParseResult parsed = PersistenceRecordV1.Parse(
                    record,
                    _profile.CodecId,
                    maximumSupportedContentVersion,
                    _profile.Limits);
                if (!parsed.IsSuccess)
                {
                    return PersistenceLoadResult<T>.Failure(parsed.ErrorCode);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var context = new PersistenceReadContext(
                    parsed.ContentVersion,
                    _profile.Limits,
                    cancellationToken);
                T value;
                try
                {
                    value = _profile.Codec.Deserialize(
                        new ReadOnlyMemory<byte>(
                            record,
                            parsed.PayloadOffset,
                            parsed.PayloadLength),
                        in context);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (PersistencePayloadBudgetExceededException exception)
                {
                    return PersistenceLoadResult<T>.Failure(
                        PersistenceErrorCode.PayloadTooLarge,
                        exception);
                }
                catch (OperationCanceledException exception)
                    when (cancellationToken.IsCancellationRequested)
                {
                    return PersistenceLoadResult<T>.Failure(
                        PersistenceErrorCode.Cancelled,
                        exception);
                }
                catch (Exception exception) when (PersistenceExceptionPolicy.IsRecoverable(exception))
                {
                    return PersistenceLoadResult<T>.Failure(
                        PersistenceErrorCode.DeserializeFailed,
                        exception);
                }

                return PersistenceLoadResult<T>.Loaded(value, parsed.ContentVersion);
            }
            catch (OperationCanceledException exception)
                when (cancellationToken.IsCancellationRequested)
            {
                return PersistenceLoadResult<T>.Failure(
                    PersistenceErrorCode.Cancelled,
                    exception);
            }
            catch (Exception exception) when (PersistenceExceptionPolicy.IsRecoverable(exception))
            {
                return PersistenceLoadResult<T>.Failure(
                    PersistenceErrorCode.ReadFailed,
                    exception);
            }
            finally
            {
                Clear(record);
                EndOperation();
            }
        }

        private async Task<PersistenceOperationResult> SaveCoreAsync(
            byte[] record,
            CancellationToken cancellationToken)
        {
            try
            {
                await _storage.WriteAtomicallyAsync(record, cancellationToken)
                    .ConfigureAwait(false);
                return PersistenceOperationResult.Success();
            }
            catch (OperationCanceledException exception)
                when (cancellationToken.IsCancellationRequested)
            {
                return PersistenceOperationResult.Failure(
                    PersistenceErrorCode.Cancelled,
                    exception);
            }
            catch (Exception exception) when (PersistenceExceptionPolicy.IsRecoverable(exception))
            {
                return PersistenceOperationResult.Failure(
                    PersistenceErrorCode.WriteFailed,
                    exception);
            }
            finally
            {
                Clear(record);
                EndOperation();
            }
        }

        private async Task<PersistenceOperationResult> DeleteCoreAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _storage.DeleteAsync(cancellationToken).ConfigureAwait(false);
                return PersistenceOperationResult.Success();
            }
            catch (OperationCanceledException exception)
                when (cancellationToken.IsCancellationRequested)
            {
                return PersistenceOperationResult.Failure(
                    PersistenceErrorCode.Cancelled,
                    exception);
            }
            catch (Exception exception) when (PersistenceExceptionPolicy.IsRecoverable(exception))
            {
                return PersistenceOperationResult.Failure(
                    PersistenceErrorCode.DeleteFailed,
                    exception);
            }
            finally
            {
                EndOperation();
            }
        }

        private void BeginOperation()
        {
            if (Interlocked.CompareExchange(ref _operationActive, 1, 0) != 0)
            {
                throw new InvalidOperationException(
                    "A PersistenceStore instance permits only one active operation.");
            }
        }

        private void EndOperation()
        {
            Volatile.Write(ref _operationActive, 0);
        }

        private static void Clear(byte[] content)
        {
            if (content != null)
            {
                Array.Clear(content, 0, content.Length);
            }
        }
    }
}
