#import <objc/runtime.h>
#import "ICadeManager.h"
#import "ICadeReaderView.h"
#import "UnityAppController.h"


@interface ICadeManager() <ICadeEventDelegate>
{
    UnityCallback stateCallback;
    ICadeReaderView * readerView;
}

@property (nonatomic, assign) BOOL activated;
@property (nonatomic, assign) NSTimer * timer;

@end



@implementation ICadeManager



+ (id) sharedManager
{
    static ICadeManager * sharedManager = nil;
    static dispatch_once_t onceToken;
    dispatch_once( & onceToken, ^{
        sharedManager = [[self alloc] init];
    });
    return sharedManager;
}


- (void) activate: (BOOL) state
{
    NSNumber * stateAsNumber = [NSNumber numberWithBool: state];
    [NSObject cancelPreviousPerformRequestsWithTarget: self];

    if (state)
    {
        [self performSelector: @selector(delayedActivate:) withObject: stateAsNumber afterDelay: 0.1];
    }
    else
    {
        [self delayedActivate: stateAsNumber];
    }
}


-(void) delayedActivate: (NSNumber *) stateAsNumber
{
    BOOL state = [stateAsNumber boolValue];

    if (state)
    {
        if (!self.activated)
        {
            self.activated = TRUE;
            readerView = [[ICadeReaderView alloc] initWithFrame: CGRectZero];
            [GetAppController().rootView addSubview: readerView];
            readerView.active = YES;
            readerView.delegate = self;
            readerView.hidden = NO;

            #if !__has_feature(objc_arc)
            [readerView release];
            #endif
        }
    }
    else
    {
        if (self.activated)
        {
            self.activated = FALSE;
            [readerView removeFromSuperview];
            readerView.active = NO;
            readerView.delegate = nil;
            readerView.hidden = YES;
        }
    }
}


- (void) setStateCallback: (UnityCallback) callback
{
    self->stateCallback = callback;
}


- (void) stateChanged: (ICadeState) state
{
    if (stateCallback != nil)
    {
        stateCallback( state );
    }
}


- (int) getState
{
    return readerView.state;
}


@end


void _SetActive( BOOL state )
{
    [[ICadeManager sharedManager] activate: state];
}


void _SetStateCallback( UnityCallback callback )
{
    [[ICadeManager sharedManager] setStateCallback: callback];
}


int _GetState()
{
    return [[ICadeManager sharedManager] getState];
}

