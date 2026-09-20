#import <Cocoa/Cocoa.h>

// Test-only bridge: exercise Cocoa's red close button and the exact delegate
// callback AppKit sends for a Dock reopen, without UI automation permissions.
int sw_close_window(void *handle)
{
    NSWindow *window = (__bridge NSWindow *)handle;
    NSButton *button = [window standardWindowButton:NSWindowCloseButton];
    if (button == nil) return 0;
    [button performClick:nil];
    return 1;
}

int sw_reopen_application(void)
{
    id<NSApplicationDelegate> delegate = NSApp.delegate;
    if (![delegate respondsToSelector:@selector(applicationShouldHandleReopen:hasVisibleWindows:)]) return 0;
    [delegate applicationShouldHandleReopen:NSApp hasVisibleWindows:NO];
    return 1;
}

int sw_window_is_visible(void *handle)
{
    NSWindow *window = (__bridge NSWindow *)handle;
    return window.isVisible && !window.isMiniaturized;
}
