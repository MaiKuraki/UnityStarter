#import <Foundation/Foundation.h>
#import <AudioToolbox/AudioToolbox.h>
#import <CoreHaptics/CoreHaptics.h>
#import <UIKit/UIKit.h>

#import "Vibration.h"

#define USING_IPAD UI_USER_INTERFACE_IDIOM() == UIUserInterfaceIdiomPad

// These symbols are implemented in HapticFeedback.mm.
extern void _impactOccurred(const char *style);
extern void _selectionChanged(void);

@interface Vibration ()
@end

@implementation Vibration

#pragma mark - Capability

+ (BOOL)hasVibrator {
    // Query Core Haptics first; the device-class heuristic is the fallback.
    if (@available(iOS 13.0, *)) {
        if (CHHapticEngine.capabilitiesForHardware.supportsHaptics) return YES;
    }
    return !(USING_IPAD);
}

#pragma mark - Fallback feedback routes

+ (void)vibrate {
    AudioServicesPlaySystemSound(kSystemSoundID_Vibrate);
}

+ (void)vibratePeek {
    _impactOccurred("Light");
}

+ (void)vibratePop {
    _impactOccurred("Heavy");
}

+ (void)vibrateNope {
    // Denial feedback: three light taps through the public generator.
    _impactOccurred("Light");
    _impactOccurred("Medium");
    _impactOccurred("Light");
}

@end

#pragma mark - "C"

extern "C" {

    bool _HasVibrator() {
        return [Vibration hasVibrator];
    }

    void _Vibrate() {
        [Vibration vibrate];
    }

    void _VibratePeek() {
        [Vibration vibratePeek];
    }

    void _VibratePop() {
        [Vibration vibratePop];
    }

    void _VibrateNope() {
        [Vibration vibrateNope];
    }

}
