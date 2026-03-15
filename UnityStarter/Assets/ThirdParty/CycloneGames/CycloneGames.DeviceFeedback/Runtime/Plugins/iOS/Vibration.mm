#import <Foundation/Foundation.h>
#import <AudioToolbox/AudioToolbox.h>
#import <UIKit/UIKit.h>

#import "Vibration.h"

#define USING_IPAD UI_USER_INTERFACE_IDIOM() == UIUserInterfaceIdiomPad

@interface Vibration ()

@end

@implementation Vibration



//////////////////////////////////////////

#pragma mark - Vibrate

+ (BOOL)    hasVibrator {
    return !(USING_IPAD);
}
+ (void)    vibrate {
    AudioServicesPlaySystemSoundWithCompletion(1352, NULL);
}
+ (void)    vibratePeek {
    AudioServicesPlaySystemSoundWithCompletion(1519, NULL); // Actuate `Peek` feedback (weak boom)
}
+ (void)    vibratePop {
    AudioServicesPlaySystemSoundWithCompletion(1520, NULL); // Actuate `Pop` feedback (strong boom)
}
+ (void)    vibrateNope {
    AudioServicesPlaySystemSoundWithCompletion(1521, NULL); // Actuate `Nope` feedback (series of three weak booms)
}

@end
/////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

#pragma mark - "C"

extern "C" {
    
    //////////////////////////////////////////
    // Vibrate
    
    bool    _HasVibrator () {
        return [Vibration hasVibrator];
    }
 
    void    _Vibrate () {
        [Vibration vibrate];
    }
    
    void    _VibratePeek () {
        [Vibration vibratePeek];
    }
    void    _VibratePop () {
        [Vibration vibratePop];
    }
    void    _VibrateNope () {
        [Vibration vibrateNope];
    }
    
}

