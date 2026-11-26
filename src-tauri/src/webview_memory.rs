use tauri::WebviewWindow;
use webview2_com::Microsoft::Web::WebView2::Win32::{
    ICoreWebView2_19, COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_LOW,
    COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_NORMAL,
};
use windows_core::Interface;

pub(crate) fn hide_to_tray(window: &WebviewWindow) -> tauri::Result<()> {
    window.hide()?;
    set_visible(window, false);
    Ok(())
}

pub(crate) fn foreground(window: &WebviewWindow) {
    set_visible(window, true);
}

fn set_visible(window: &WebviewWindow, visible: bool) {
    // with_webview runs on the controller's UI thread. Older runtimes lack
    // ICoreWebView2_19 and keep their existing memory policy.
    let _ = window.with_webview(move |webview| unsafe {
        let controller = webview.controller();
        // Hiding the native parent window alone does not notify the WebView2 controller.
        let _ = controller.SetIsVisible(visible);
        if let Ok(core) = controller.CoreWebView2() {
            if let Ok(core) = core.cast::<ICoreWebView2_19>() {
                let level = if visible {
                    COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_NORMAL
                } else {
                    COREWEBVIEW2_MEMORY_USAGE_TARGET_LEVEL_LOW
                };
                let _ = core.SetMemoryUsageTargetLevel(level);
            }
        }
    });
}
