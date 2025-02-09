#import "ICadeReaderView.h"
#import <UIKit/UIKit.h>

static const char * DN_INPUTS = "wdxayhujikol";
static const char * UP_INPUTS = "eczqtrfnmpgv";

@interface ICadeReaderView()

- (void) willResignActive;
- (void) didBecomeActive;

@end


@implementation ICadeReaderView

@synthesize state = _state, delegate = _delegate, active;


- (id) initWithFrame: (CGRect) frame
{
    self = [super initWithFrame: frame];
    inputView = [[UIView alloc] initWithFrame: CGRectZero];

    [[NSNotificationCenter defaultCenter]
         addObserver: self
         selector: @selector(willResignActive)
         name: UIApplicationWillResignActiveNotification
         object: nil
     ];

    [[NSNotificationCenter defaultCenter]
         addObserver: self
         selector: @selector(didBecomeActive)
         name: UIApplicationDidBecomeActiveNotification
         object: nil
     ];

    return self;
}


- (void) dealloc
{
    [[NSNotificationCenter defaultCenter]
         removeObserver: self
         name: UIApplicationWillResignActiveNotification
         object: nil
     ];

    [[NSNotificationCenter defaultCenter]
         removeObserver: self
         name: UIApplicationDidBecomeActiveNotification
         object: nil
     ];

#if !__has_feature(objc_arc)
    [super dealloc];
#endif
}


- (void) willResignActive
{
    if (self.active)
    {
        [self resignFirstResponder];
    }
}


- (void) didBecomeActive
{
    if (self.active)
    {
        [self becomeFirstResponder];
    }
}


- (BOOL) canBecomeFirstResponder
{
    return YES;
}


- (void) setActive: (BOOL) value
{
    if (active == value)
    {
        if (value)
        {
            [self resignFirstResponder];
        }
        else
        {
            return;
        }
    }

    active = value;
    if (active)
    {
        if ([[UIApplication sharedApplication] applicationState] == UIApplicationStateActive)
        {
            [self becomeFirstResponder];
        }
    }
    else
    {
        [self resignFirstResponder];
    }
}


- (UIView *) inputView
{
    return inputView;
}


- (BOOL) hasText
{
    return NO;
}


- (void) insertText: (NSString *) text
{
}


- (void) deleteBackward
{
}


- (NSArray *) keyCommands
{
    NSMutableArray * keys = [NSMutableArray array];

    int numberOfStates = (int)(strlen(DN_INPUTS) + strlen(UP_INPUTS));
    char states[numberOfStates + 1];
    strcpy( states, DN_INPUTS );
    strcat( states, UP_INPUTS );

    for (int i = 0; i < numberOfStates; i++)
    {
        UIKeyCommand * keyCommand = [UIKeyCommand keyCommandWithInput: [NSString stringWithFormat: @"%c" , states[i]] modifierFlags: 0 action: @selector(keyPressed:)];
        [keys addObject: keyCommand];
    }

    return keys;
}


- (void) keyPressed: (UIKeyCommand *) keyCommand
{
    bool stateChanged = false;
    char input = [keyCommand.input characterAtIndex: 0];

    for (int i = 0; i < 12; i++)
    {
        if (input == DN_INPUTS[i])
        {
            _state |= (1 << i);
            stateChanged = true;
            break;
        }

        if (input == UP_INPUTS[i])
        {
            _state &= ~(1 << i);
            stateChanged = true;
            break;
        }
    }

    if (stateChanged)
    {
        [_delegate stateChanged: _state];
    }

    static int cycleResponder = 0;
    if (++cycleResponder > 20)
    {
        // necessary to clear a buffer that accumulates internally
        cycleResponder = 0;
        [self resignFirstResponder];
        [self becomeFirstResponder];
    }
}

@end

