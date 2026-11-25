using System;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.Persistence
{
    public sealed class PersistenceStore<T>
    {
        private readonly IPersistenceStorage _storage;
        private readonly PersistenceProfile<T> _profile;
        private readonly BoundedByteBufferWriter _writer;
        private int _operationActive;
        private long _startedLoadCount;
        private long _startedSaveCount;
        private long _startedDeleteCount;
        private long _concurrentOperationRejectionCount;
        private long _lastRecordBytes;
        private long _peakRecordBytes;

        public PersistenceStore(
            IPersistenceStorage storage,
            PersistenceProfile<T> profile)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
            _writer = new BoundedByteBufferWriter(
                profile.Limits.InitialBufferBytes,
                profile.Limits.MaximumPayloadBytes);
        }

        public string Location => _storage.Location;

        public PersistenceProfile<T> Profile => _profile;

        public PersistenceStoreMemorySnapshot GetMemorySnapshot()
        {
            PersistenceLimits limits = _profile.Limits;
            return new PersistenceStoreMemorySnapshot(
                Volatile.Read(ref _operationActive) != 0,
                limits.MaximumPayloadBytes,
                limits.MaximumRecordBytes,
                Interlocked.Read(ref _startedLoadCount),
                Interlocked.Read(ref _startedSaveCount),
                Interlocked.Read(ref _startedDeleteCount),
                Interlocked.Read(ref _concurrentOperationRejectionCount),
                Interlocked.Read(ref _lastRecordBytes),
                Interlocked.Read(ref _peakRecordBytes));
        }

        public Task<PersistenceLoadResult<T>> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            BeginOperation();
            Interlocked.Increment(ref _startedLoadCount);
            try
            {
                return LoadCoreAsync(cancellationToken);
            }
            catch
            {
                EndOperation();
                throw;
            }
        }

        public Task<PersistenceOperationResult> SaveAsync(
            in T value,
            CancellationToken cancellationToken = default)
        {
            BeginOperation();
            Interlocked.Increment(ref _startedSaveCount);
            byte[] record = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                PersistenceLimits limits = _profile.Limits;
                var context = new PersistenceWriteContext(limits, cancellationToken);
                // The gate serializes operations on this instance, so one pooled writer is reused
                // across saves. Reset/Release keep the hot path free of writer-object allocation.
                _writer.Reset();
                try
                {
                    _profile.Codec.Serialize(in value, _writer, in context);
                    cancellationToken.ThrowIfCancellationRequested();
                    record = PersistenceRecord.Encode(
                        _writer.WrittenSpan,
                        _profile.CodecId,
                        limits);
                    ObserveRecordBytes(record.Length);
                }
                finally
                {
                    _writer.Release();
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
            Interlocked.Increment(ref _startedDeleteCount);
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

                ObserveRecordBytes(record.Length);

                if (record.Length > _profile.Limits.MaximumRecordBytes)
                {
                    return PersistenceLoadResult<T>.Failure(
                        PersistenceErrorCode.PayloadTooLarge);
                }

                cancellationToken.ThrowIfCancellationRequested();
                // Parse runs before the codec sees any payload memory; parse failures never call it.
                PersistenceRecordParseResult parsed = PersistenceRecord.Parse(
                    record,
                    _profile.CodecId,
                    _profile.Limits);
                if (!parsed.IsSuccess)
                {
                    return PersistenceLoadResult<T>.Failure(parsed.ErrorCode);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var context = new PersistenceReadContext(_profile.Limits, cancellationToken);
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

                return PersistenceLoadResult<T>.Loaded(value);
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

        // The storage write is the commit boundary. A successful replacement reports success even
        // when the caller token is cancelled afterwards.
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

        // The guard covers one Store instance. Stores sharing a storage or codec instance are not
        // serialized against each other; the composition root owns that policy.
        private void BeginOperation()
        {
            if (Interlocked.CompareExchange(ref _operationActive, 1, 0) != 0)
            {
                Interlocked.Increment(ref _concurrentOperationRejectionCount);
                throw new InvalidOperationException(
                    "A PersistenceStore instance permits only one active operation.");
            }
        }

        private void EndOperation()
        {
            Volatile.Write(ref _operationActive, 0);
        }

        private void ObserveRecordBytes(int recordBytes)
        {
            Interlocked.Exchange(ref _lastRecordBytes, recordBytes);
            long currentPeak = Interlocked.Read(ref _peakRecordBytes);
            while (recordBytes > currentPeak)
            {
                long observed = Interlocked.CompareExchange(
                    ref _peakRecordBytes,
                    recordBytes,
                    currentPeak);
                if (observed == currentPeak)
                {
                    return;
                }

                currentPeak = observed;
            }
        }

        // Read buffers transferred from storage and write buffers created here are both owned by
        // the Store and cleared once the operation completes.
        private static void Clear(byte[] content)
        {
            if (content != null)
            {
                Array.Clear(content, 0, content.Length);
            }
        }
    }
}
