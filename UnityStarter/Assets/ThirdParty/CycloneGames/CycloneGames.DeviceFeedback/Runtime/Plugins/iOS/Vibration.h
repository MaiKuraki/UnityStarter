#import <Foundation/Foundation.h>

@interface Vibration : NSObject

//////////////////////////////////////////

#pragma mark - Vibrate

+ (BOOL)    hasVibrator;
+ (void)    vibrate;
+ (void)    vibratePeek;
+ (void)    vibratePop;
+ (void)    vibrateNope;

//////////////////////////////////////////


@end

