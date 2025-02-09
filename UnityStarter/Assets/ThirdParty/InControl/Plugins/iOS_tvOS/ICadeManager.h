#import <Foundation/Foundation.h>


typedef void (* UnityCallback)( int state );


@interface ICadeManager : NSObject

+(id) sharedManager;
-(void) activate: (BOOL) state;
-(void) delayedActivate: (NSNumber *) stateNumber;
-(void) setStateCallback: (UnityCallback) callback;
-(int) getState;

@end


// END


