#import <Foundation/Foundation.h>
#import <CoreHaptics/CoreHaptics.h>

// Persistent engine — CHHapticEngine is expensive to create; reuse across the app lifetime.
// s_continuousPlayer: the long-lived player for continuous/pattern effects (stoppable).
// Transient haptics use ephemeral fire-and-forget players — no stop needed.
static CHHapticEngine *s_engine API_AVAILABLE(ios(13.0)) = nil;
static id<CHHapticPatternPlayer> s_continuousPlayer API_AVAILABLE(ios(13.0)) = nil;
static BOOL s_engineRunning = NO;

static void EnsureEngine(void) API_AVAILABLE(ios(13.0)) {
    if (s_engine != nil) return;

    NSError *error = nil;
    s_engine = [[CHHapticEngine alloc] initAndReturnError:&error];
    if (error != nil || s_engine == nil) {
        s_engine = nil;
        return;
    }

    // Auto-restart on interruption (phone call, etc.)
    s_engine.resetHandler = ^{
        NSError *startErr = nil;
        [s_engine startAndReturnError:&startErr];
        s_engineRunning = (startErr == nil);
    };

    s_engine.stoppedHandler = ^(CHHapticEngineStoppedReason reason) {
        s_engineRunning = NO;
    };
}

static void EnsureRunning(void) API_AVAILABLE(ios(13.0)) {
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

extern "C" {

bool _CoreHapticsSupported() {
    if (@available(iOS 13.0, *)) {
        return CHHapticEngine.capabilitiesForHardware.supportsHaptics;
    }
    return false;
}

void _CoreHapticsInit() {
    if (@available(iOS 13.0, *)) {
        EnsureRunning();
    }
}

void _CoreHapticsDestroy() {
    if (@available(iOS 13.0, *)) {
        StopContinuousPlayer();
        if (s_engine != nil) {
            [s_engine stopWithCompletionHandler:nil];
            s_engine = nil;
            s_engineRunning = NO;
        }
    }
}

// Fire-and-forget transient — does NOT stop the continuous player.
// Creates an ephemeral player for minimum latency on rapid taps.
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

        // Ephemeral player — no need to track or stop, engine auto-releases after completion
        id<CHHapticPatternPlayer> player = [s_engine createPlayerWithPattern:pattern error:&error];
        if (error != nil) return;

        [player startAtTime:CHHapticTimeImmediate error:&error];
    }
}

// Continuous event — stoppable via s_continuousPlayer, supports UpdateParameters.
void _CoreHapticsPlayContinuous(float intensity, float sharpness, float duration) {
    if (@available(iOS 13.0, *)) {
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

// Composite pattern from marshaled arrays.
// types: 0=Transient, 1=Continuous
void _CoreHapticsPlayPattern(const float *times, const float *intensities,
                              const float *sharpnesses, const int *types,
                              const float *durations, int count) {
    if (@available(iOS 13.0, *)) {
        if (count <= 0) return;
        EnsureRunning();
        if (s_engine == nil) return;

        StopContinuousPlayer();

        NSMutableArray<CHHapticEvent *> *events = [NSMutableArray arrayWithCapacity:count];

        for (int i = 0; i < count; i++) {
            CHHapticEventParameter *ip =
                [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticIntensity
                                                             value:intensities[i]];
            CHHapticEventParameter *sp =
                [[CHHapticEventParameter alloc] initWithParameterID:CHHapticEventParameterIDHapticSharpness
                                                             value:sharpnesses[i]];

            CHHapticEventType eventType = (types[i] == 0) ? CHHapticEventTypeHapticTransient
                                                          : CHHapticEventTypeHapticContinuous;

            CHHapticEvent *event;
            if (types[i] == 0) {
                event = [[CHHapticEvent alloc] initWithEventType:eventType
                                                     parameters:@[ip, sp]
                                                   relativeTime:times[i]];
            } else {
                event = [[CHHapticEvent alloc] initWithEventType:eventType
                                                     parameters:@[ip, sp]
                                                   relativeTime:times[i]
                                                       duration:durations[i]];
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

// Dual parameter curves (intensity + sharpness) with OS-level smooth interpolation.
void _CoreHapticsPlayCurves(const float *times, const float *intensities,
                             const float *sharpnesses, int pointCount, float duration) {
    if (@available(iOS 13.0, *)) {
        if (pointCount <= 0 || duration <= 0) return;
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
            [NSMutableArray arrayWithCapacity:pointCount];
        NSMutableArray<CHHapticParameterCurveControlPoint *> *sharpnessPoints =
            [NSMutableArray arrayWithCapacity:pointCount];

        for (int i = 0; i < pointCount; i++) {
            CHHapticParameterCurveControlPoint *ip =
                [[CHHapticParameterCurveControlPoint alloc] initWithRelativeTime:times[i]
                                                                          value:intensities[i]];
            CHHapticParameterCurveControlPoint *sp =
                [[CHHapticParameterCurveControlPoint alloc] initWithRelativeTime:times[i]
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
        CHHapticPattern *pattern =
            [[CHHapticPattern alloc] initWithEvents:@[baseEvent]
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

// Real-time parameter update on the active continuous player
void _CoreHapticsUpdateParameters(float intensity, float sharpness) {
    if (@available(iOS 13.0, *)) {
        if (s_continuousPlayer == nil) return;

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
