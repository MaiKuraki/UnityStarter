#import <UIKit/UIKit.h>

// Pre-allocated generators — calling prepare() ahead of time lets the Taptic Engine
// spin up before the trigger, eliminating ~30-50ms cold-start latency.
// Apple docs: "Call prepare() before the event that triggers feedback."

static UIImpactFeedbackGenerator *s_impactLight   = nil;
static UIImpactFeedbackGenerator *s_impactMedium  = nil;
static UIImpactFeedbackGenerator *s_impactHeavy   = nil;
static UIImpactFeedbackGenerator *s_impactRigid   = nil;  // iOS 13+
static UIImpactFeedbackGenerator *s_impactSoft    = nil;  // iOS 13+
static UINotificationFeedbackGenerator *s_notification = nil;
static UISelectionFeedbackGenerator    *s_selection     = nil;

static void EnsureGenerators(void) {
    if (s_impactLight != nil) return;

    s_impactLight  = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
    s_impactMedium = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
    s_impactHeavy  = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];

    if (@available(iOS 13.0, *)) {
        s_impactRigid = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleRigid];
        s_impactSoft  = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleSoft];
    }

    s_notification = [[UINotificationFeedbackGenerator alloc] init];
    s_selection    = [[UISelectionFeedbackGenerator alloc] init];

    // Pre-warm all generators
    [s_impactLight prepare];
    [s_impactMedium prepare];
    [s_impactHeavy prepare];
    if (s_impactRigid != nil) [s_impactRigid prepare];
    if (s_impactSoft  != nil) [s_impactSoft prepare];
    [s_notification prepare];
    [s_selection prepare];
}

// Re-prepare after each trigger so the next call is also low-latency.
static inline void RePrepareImpact(UIImpactFeedbackGenerator *gen) {
    if (gen != nil) [gen prepare];
}

extern "C" {

void _HapticFeedbackPrepare() {
    EnsureGenerators();
}

void _impactOccurred(const char *style)
{
    EnsureGenerators();

    UIImpactFeedbackGenerator *gen = nil;

    if      (strcmp(style, "Heavy")  == 0) gen = s_impactHeavy;
    else if (strcmp(style, "Medium") == 0) gen = s_impactMedium;
    else if (strcmp(style, "Light")  == 0) gen = s_impactLight;
    else if (strcmp(style, "Rigid")  == 0) gen = s_impactRigid;
    else if (strcmp(style, "Soft")   == 0) gen = s_impactSoft;

    if (gen == nil) return;

    [gen impactOccurred];
    RePrepareImpact(gen);
}

void _notificationOccurred(const char *style)
{
    EnsureGenerators();

    UINotificationFeedbackType feedbackStyle;
    if (strcmp(style, "Error") == 0)
        feedbackStyle = UINotificationFeedbackTypeError;
    else if (strcmp(style, "Success") == 0)
        feedbackStyle = UINotificationFeedbackTypeSuccess;
    else if (strcmp(style, "Warning") == 0)
        feedbackStyle = UINotificationFeedbackTypeWarning;
    else
        return;

    [s_notification notificationOccurred:feedbackStyle];
    [s_notification prepare];
}

void _selectionChanged()
{
    EnsureGenerators();

    [s_selection selectionChanged];
    [s_selection prepare];
}
}
