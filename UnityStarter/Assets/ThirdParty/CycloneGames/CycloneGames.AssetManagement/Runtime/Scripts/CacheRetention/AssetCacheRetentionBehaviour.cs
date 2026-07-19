using System;
using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime.CacheRetention
{
    /// <summary>
    /// Thin, opt-in MonoBehaviour that drives asset cache retention from a scene object.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AssetCacheRetentionBehaviour : MonoBehaviour
    {
        private const double DEFAULT_MINIMUM_IDLE_SECONDS = 120d;
        private const double DEFAULT_CHECK_INTERVAL_SECONDS = 30d;
        private const double MINIMUM_CHECK_INTERVAL_SECONDS = 1d;
        private const double MAXIMUM_RETENTION_SECONDS = 365d * 24d * 60d * 60d;

        [SerializeField, Tooltip("Idle handles retained for at least this many seconds are evicted. Clamped from 0 to 365 days; 0 evicts all matched idle handles.")]
        private double MinimumIdleSeconds = DEFAULT_MINIMUM_IDLE_SECONDS;

        [SerializeField, Tooltip("Seconds between retention passes. Clamped from 1 second to 365 days.")]
        private double CheckIntervalSeconds = DEFAULT_CHECK_INTERVAL_SECONDS;

        [SerializeField, Tooltip("Optional bucket to trim. Empty applies the policy globally.")]
        private string Bucket = string.Empty;

        [SerializeField, Tooltip("When Bucket is set, also match child buckets such as UI.Shop under UI.")]
        private bool IncludeChildBuckets = true;

        [SerializeField, Tooltip("Log how many handles each non-empty retention pass evicted.")]
        private bool LogEvictions = false;

        private AssetCacheRetentionScheduler _scheduler;
        private IAssetPackage _boundPackage;

        private void OnEnable()
        {
            if (_boundPackage != null)
            {
                RestartScheduler();
            }
        }

        private void OnDisable()
        {
            _scheduler?.Stop();
        }

        private void OnDestroy()
        {
            _scheduler?.Dispose();
            _scheduler = null;
        }

        private void OnValidate()
        {
            MinimumIdleSeconds = NormalizeSeconds(
                MinimumIdleSeconds,
                DEFAULT_MINIMUM_IDLE_SECONDS,
                0d);
            CheckIntervalSeconds = NormalizeSeconds(
                CheckIntervalSeconds,
                DEFAULT_CHECK_INTERVAL_SECONDS,
                MINIMUM_CHECK_INTERVAL_SECONDS);
        }

        /// <summary>
        /// Binds an explicit package and restarts the scheduler when the behaviour is active.
        /// </summary>
        public void Bind(IAssetPackage package)
        {
            _boundPackage = package ?? throw new ArgumentNullException(nameof(package));
            if (isActiveAndEnabled)
            {
                RestartScheduler();
            }
        }

        private void RestartScheduler()
        {
            _scheduler?.Dispose();
            _scheduler = new AssetCacheRetentionScheduler(
                _boundPackage,
                BuildPolicy(),
                TimeSpan.FromSeconds(NormalizeSeconds(
                    CheckIntervalSeconds,
                    DEFAULT_CHECK_INTERVAL_SECONDS,
                    MINIMUM_CHECK_INTERVAL_SECONDS)),
                LogEvictions);
            _scheduler.Start();
        }

        private AssetCacheRetentionPolicy BuildPolicy()
        {
            var idleRule = AssetCacheRetentionRules.IdleForAtLeast(TimeSpan.FromSeconds(NormalizeSeconds(
                MinimumIdleSeconds,
                DEFAULT_MINIMUM_IDLE_SECONDS,
                0d)));
            if (string.IsNullOrEmpty(Bucket))
            {
                return new AssetCacheRetentionPolicy(idleRule);
            }

            return AssetCacheRetentionPolicy.MatchingAll(
                idleRule,
                AssetCacheRetentionRules.Bucket(Bucket, IncludeChildBuckets));
        }

        private static double NormalizeSeconds(double seconds, double fallback, double minimum)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                return fallback;
            }

            return Math.Clamp(seconds, minimum, MAXIMUM_RETENTION_SECONDS);
        }
    }
}
