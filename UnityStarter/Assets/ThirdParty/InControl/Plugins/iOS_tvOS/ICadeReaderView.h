#import <UIKit/UIKit.h>


typedef enum ICadeState
{
    None      = 0x000,
    DPadUp    = 0x001,
    DPadRight = 0x002,
    DPadDown  = 0x004,
    DPadLeft  = 0x008,
    Action0   = 0x010,
    Action1   = 0x020,
    Action2   = 0x040,
    Action3   = 0x080,
    Action4   = 0x100,
    Action5   = 0x200,
    Action6   = 0x400,
    Action7   = 0x800,

} ICadeState;


@protocol ICadeEventDelegate <NSObject>

- (void) stateChanged: (ICadeState) state;

@end


@interface ICadeReaderView : UIView<UIKeyInput>
{
    UIView                  * inputView;
    ICadeState              _state;
    id<ICadeEventDelegate>  _delegate;
}

@property (nonatomic, assign) ICadeState state;
@property (nonatomic, strong) id<ICadeEventDelegate> delegate;
@property (nonatomic, assign) BOOL active;

@end


// END

