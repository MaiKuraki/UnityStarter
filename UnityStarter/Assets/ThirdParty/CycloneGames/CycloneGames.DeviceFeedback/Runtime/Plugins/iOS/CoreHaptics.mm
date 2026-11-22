#import <Foundation/Foundation.h>
#import <CoreHaptics/CoreHaptics.h>

// All exported functions are main-thread-only; the C# layer enforces owner-thread
// confinement. Engine callbacks never mutate state after shutdown.
// s_transientPlayers holds strong references to in-flight transient players so
// ARC cannot release a player during playback. The set is bounded.

static const NSUInteger kMaxHapticEvents = 16384;
static const NSUInteger kMaxRetainedTransientPlayers = 8;

static CHHapticEngine *s_engine API_AVAILABLE(ios(13.0)) = nil;
static id<CHHapticPatternPlayer> s_continuousPlayer API_AVAILABLE(ios(13.0)) = nil;
static NSMutableArray<id<CHHapticPatternPlayer>> *s_transientPlayers API_AVAILABLE(ios(13.0)) = nil;
static BOOL s_engineRunning = NO;
static BOOL s_shuttingDown = NO;

static void EnsureEngine(void) API_AVAILABLE(ios(13.0)) {
    if (s_engine != nil) return;
    if (s_transientPlayers == nil) {
        s_transientPlayers = [NSMutableArray arrayWithCapacity:kMaxRetainedTransientPlayers];
    }

    NSError *error = nil;
    s_engine = [[CHHapticEngine alloc] initAndReturnError:&error];
    if (error != nil || s_engine == nil) {
        s_engine = nil;
        return;
    }

    __weak typeof(s_engine) weakEngine = s_engine;
    s_engine.resetHandler = ^{
        if (s_shuttingDown) return;
        CHHapticEngine *engine = weakEngine;
        if (engine == nil) return;
        NSError *startErr = nil;
        [engine startAndReturnError:&startErr];
        s_engineRunning = (startErr == nil);
    };

    s_engine.stoppedHandler = ^(CHHapticEngineStoppedReason reason) {
        if (s_shuttingDown) return;
        s_engineRunning = NO;
    };
}

static void EnsureRunning(void) API_AVAILABLE(ios(13.0)) {
    if (s_shuttingDown) return;
    EnsureEngine();
    if (s_engine == nil) return;
    if (!s_engineRunning) {
        NSError *error = nil;
        [s_engine startAndReturnError:&error];
        s_engineRunning = (error == nil);
    }
}

static void StopContinuousPlayer(void) API_AVAILABLE(ios(13.0)) {
    if (s_continuousPlayer != nil) {
        NSError *error = nil;
        [s_continuousPlayer stopAtTime:0 error:&error];
        s_continuousPlayer = nil;
    }
}

static void RetainTransientPlayer(id<CHHapticPatternPlayer> player) API_AVAILABLE(ios(13.0)) {
    if (player == nil) return;
    if (s_transientPlayers == nil) {
        s_transientPlayers = [NSMutableArray arrayWithCapacity:kMaxRetainedTransientPlayers];
    }
    [s_transientPlayers addObject:player];
    while (s_transientPlayers.count > kMaxRetainedTransientPlayers) {
        [s_transientPlayers removeObjectAtIndex:0];
    }
}

static BOOL ValidEventBatch(int count, const float *times, const float *intensities,
                            const float *sharpnesses) {
    // Native ABI is an untrusted boundary: validate the count and the pointers
    // before allocating any Objective-C objects.
    if (count <= 0 || (NSUInteger)count > kMaxHapticEvents) return NO;
    if (times == NULL || intensities == NULL || sharpnesses == NULL) return NO;
    return YES;
}

extern "C" {

bool _CoreHapticsSupported() {
    if (@available(iOS 13.0, *)) {
        return CHHapticEngine.capabilitiesForHardware.supportsHaptics;
    }
    return false;
}

void _CoreHapticsInit() {
    if (@available(iOS 13.0, *)) {
        s_shuttingDown = NO;
        EnsureRunning();
    }
}

void _CoreHapticsDestroy() {
    if (@available(iOS 13.0, *)) {
        s_shuttingDown = YES;              // suppress reset/stopped callbacks
        StopContinuousPlayer();
        [s_transientPlayers removeAllObjects];
        if (s_engine != nil) {
            [s_engine stopWithCompletionHandler:nil];
            s_engine = nil;
        }
        s_engineRunning = NO;
    }
}

void _CoreHapticsPlayTransient(float intensity, float sharpness) {
    if (@available(iOS 13.0, *)) {
        EnsureRunning();
        if (s_engine == nil) return;

        CHHapticEventParameter *intensityParam =
            [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity
                                                         value:intensity];
        CHHapticEventParameter *sharpnessParam =
            [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness
                                                         value:sharpness];
        CHHapticEvent *event =
            [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticTransient
                                          parameters:@[intensityParam, sharpnessParam]
                                        relativeTime:0];
        NSError *error = nil;
        CHHapticPattern *pattern = [[CHHapticPattern alloc] initWithEvents:@[event] parameters:@[] error:&error];
        if (error != nil) return;

        id<CHHapticPatternPlayer> player = [s_engine createPlayerWithPattern:pattern error:&error];
        if (error != nil || player == nil) return;

        RetainTransientPlayer(player);     // keep alive until a later call evicts it
        [player startAtTime:CHHapticTimeImmediate error:&error];
    }
}

void _CoreHapticsPlayContinuous(float intensity, float sharpness, float duration) {
    if (@available(iOS 13.0, *)) {
        if (duration <= 0) return;
        EnsureRunning();
        if (s_engine == nil) return;
        StopContinuousPlayer();

        CHHapticEventParameter *intensityParam =
            [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity
                                                         value:intensity];
        CHHapticEventParameter *sharpnessParam =
            [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness
                                                         value:sharpness];
        CHHapticEvent *event =
            [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticContinuous
                                          parameters:@[intensityParam, sharpnessParam]
                                        relativeTime:0
                                            duration:duration];
        NSError *error = nil;
        CHHapticPattern *pattern = [[CHHapticPattern alloc] initWithEvents:@[event] parameters:@[] error:&error];
        if (error != nil) return;

        s_continuousPlayer = [s_engine createPlayerWithPattern:pattern error:&error];
        if (error != nil) { s_continuousPlayer = nil; return; }
        [s_continuousPlayer startAtTime:CHHapticTimeImmediate error:&error];
    }
}

void _CoreHapticsPlayPattern(const float *times, const float *intensities,
                              const float *sharpnesses, const int *types,
                              const float *durations, int count) {
    if (@available(iOS 13.0, *)) {
        if (!ValidEventBatch(count, times, intensities, sharpnesses)) return;
        if (types == NULL || durations == NULL) return;
        EnsureRunning();
        if (s_engine == nil) return;
        StopContinuousPlayer();

        NSMutableArray<CHHapticEvent *> *events = [NSMutableArray arrayWithCapacity:(NSUInteger)count];
        for (int i = 0; i < count; i++) {
            float intensity = intensities[i];
            float sharpness = sharpnesses[i];
            // C# admission rejects non-finite values; clamp defensively here too.
            if (intensity < 0.0f) intensity = 0.0f;
            if (intensity > 1.0f) intensity = 1.0f;
            if (sharpness < 0.0f) sharpness = 0.0f;
            if (sharpness > 1.0f) sharpness = 1.0f;

            CHHapticEventParameter *ip =
                [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity
                                                             value:intensity];
            CHHapticEventParameter *sp =
                [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness
                                                             value:sharpness];
            CHHapticEventType eventType = (types[i] == 0) ? CHHapticEventTypeHapticTransient
                                                          : CHHapticEventTypeHapticContinuous;
            CHHapticEvent *event;
            float relativeTime = times[i] < 0.0f ? 0.0f : times[i];
            if (types[i] == 0) {
                event = [[CHHapticEvent alloc] initWithEventType:eventType
                                                     parameters:@[ip, sp]
                                                   relativeTime:relativeTime];
            } else {
                float eventDuration = durations[i] > 0.0f ? durations[i] : 0.01f;
                event = [[CHHapticEvent alloc] initWithEventType:eventType
                                                     parameters:@[ip, sp]
                                                   relativeTime:relativeTime
                                                       duration:eventDuration];
            }
            [events addObject:event];
        }

        NSError *error = nil;
        CHHapticPattern *pattern = [[CHHapticPattern alloc] initWithEvents:events parameters:@[] error:&error];
        if (error != nil) return;
        s_continuousPlayer = [s_engine createPlayerWithPattern:pattern error:&error];
        if (error != nil) { s_continuousPlayer = nil; return; }
        [s_continuousPlayer startAtTime:CHHapticTimeImmediate error:&error];
    }
}

void _CoreHapticsPlayCurves(const float *times, const float *intensities,
                             const float *sharpnesses, int pointCount, float duration) {
    if (@available(iOS 13.0, *)) {
        if (!ValidEventBatch(pointCount, times, intensities, sharpnesses)) return;
        if (duration <= 0) return;
        EnsureRunning();
        if (s_engine == nil) return;
        StopContinuousPlayer();

        CHHapticEventParameter *baseIntensity =
            [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity
                                                         value:intensities[0]];
        CHHapticEventParameter *baseSharpness =
            [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness
                                                         value:sharpnesses[0]];
        CHHapticEvent *baseEvent =
            [[CHHapticEvent alloc] initWithEventType:CHHapticEventTypeHapticContinuous
                                          parameters:@[baseIntensity, baseSharpness]
                                        relativeTime:0
                                            duration:duration];

        NSMutableArray<CHHapticParameterCurveControlPoint *> *intensityPoints =
            [NSMutableArray arrayWithCapacity:(NSUInteger)pointCount];
        NSMutableArray<CHHapticParameterCurveControlPoint *> *sharpnessPoints =
            [NSMutableArray arrayWithCapacity:(NSUInteger)pointCount];
        for (int i = 0; i < pointCount; i++) {
            float relativeTime = times[i] < 0.0f ? 0.0f : times[i];
            if (relativeTime > duration) relativeTime = duration;
            CHHapticParameterCurveControlPoint *ip =
                [[CHHapticParameterCurveControlPoint alloc] initWithRelativeTime:relativeTime
                                                                          value:intensities[i]];
            CHHapticParameterCurveControlPoint *sp =
                [[CHHapticParameterCurveControlPoint alloc] initWithRelativeTime:relativeTime
                                                                          value:sharpnesses[i]];
            [intensityPoints addObject:ip];
            [sharpnessPoints addObject:sp];
        }

        CHHapticParameterCurve *intensityCurve =
            [[CHHapticParameterCurve alloc] initWithParameterID:CHHapticDynamicParameterIDHapticIntensityControl
                                                 controlPoints:intensityPoints
                                                  relativeTime:0];
        CHHapticParameterCurve *sharpnessCurve =
            [[CHHapticParameterCurve alloc] initWithParameterID:CHHapticDynamicParameterIDHapticSharpnessControl
                                                 controlPoints:sharpnessPoints
                                                  relativeTime:0];

        NSError *error = nil;
        CHHapticPattern *pattern = [[CHHapticPattern alloc] initWithEvents:@[baseEvent]
                                                            parameterCurves:@[intensityCurve, sharpnessCurve]
                                                                      error:&error];
        if (error != nil) return;
        s_continuousPlayer = [s_engine createPlayerWithPattern:pattern error:&error];
        if (error != nil) { s_continuousPlayer = nil; return; }
        [s_continuousPlayer startAtTime:CHHapticTimeImmediate error:&error];
    }
}

void _CoreHapticsStop() {
    if (@available(iOS 13.0, *)) {
        StopContinuousPlayer();
    }
}

// CHHapticDynamicParameter.value is immutable, so each call creates two
// parameter objects. The container array is reused.
void _CoreHapticsUpdateParameters(float intensity, float sharpness) {
    if (@available(iOS 13.0, *)) {
        if (s_continuousPlayer == nil || s_shuttingDown) return;
        CHHapticDynamicParameter *ip =
            [[CHHapticDynamicParameter alloc] initWithParameterID:CHHapticDynamicParameterIDHapticIntensityControl
                                                           value:intensity
                                                    relativeTime:0];
        CHHapticDynamicParameter *sp =
            [[CHHapticDynamicParameter alloc] initWithParameterID:CHHapticDynamicParameterIDHapticSharpnessControl
                                                           value:sharpness
                                                    relativeTime:0];
        NSError *error = nil;
        [s_continuousPlayer sendParameters:@[ip, sp] atTime:CHHapticTimeImmediate error:&error];
    }
}

} // extern "C"
