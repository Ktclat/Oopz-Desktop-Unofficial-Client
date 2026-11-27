//! Windows can keep its native tray alive without owning a browser window.
use std::{mem, sync::Mutex};

use tauri::{AppHandle, Manager, RunEvent, WindowEvent};

use crate::{create_main_window, restore, PageMenuItems, MAIN_WINDOW, TRAY_ID};

#[derive(Default)]
pub(crate) struct Session(Mutex<Transition>);

// Window creation and destruction finish asynchronously. Preserve the last menu
// choice while either operation is in flight, including a second-instance activation.
#[derive(Default)]
enum Transition {
    #[default]
    Ready,
    Opening {
        unload_after: bool,
    },
    Closing {
        reopen_after: bool,
    },
}

pub(crate) fn open(app: &AppHandle) {
    request(app, true);
}

pub(crate) fn unload(app: &AppHandle) {
    request(app, false);
}

fn request(app: &AppHandle, online: bool) {
    let state = app.state::<Session>();
    let mut transition = state.0.lock().unwrap();
    match &mut *transition {
        Transition::Opening { unload_after } => {
            *unload_after = !online;
            return;
        }
        Transition::Closing { reopen_after } => {
            *reopen_after = online;
            return;
        }
        Transition::Ready => {}
    }

    match (online, app.get_webview_window(MAIN_WINDOW)) {
        (true, Some(window)) => {
            drop(transition);
            restore(&window);
        }
        (true, None) => {
            *transition = Transition::Opening {
                unload_after: false,
            };
            drop(transition);
            let app = app.clone();
            // WebView2 construction in a synchronous Windows UI callback can deadlock.
            tauri::async_runtime::spawn_blocking(move || {
                let result = create_main_window(&app);
                let handle = app.clone();
                let _ = app.run_on_main_thread(move || finish_open(&handle, result));
            });
        }
        (false, Some(window)) => {
            *transition = Transition::Closing {
                reopen_after: false,
            };
            drop(transition);
            // Windows destroys any HMENU still attached to a destroyed HWND.
            // Detach the shared app menu so the next window can use it again.
            let result = window.remove_menu().and_then(|menu| {
                window.destroy().inspect_err(|_| {
                    if let Some(menu) = menu {
                        let _ = window.set_menu(menu);
                    }
                })
            });
            if let Err(error) = result {
                *state.0.lock().unwrap() = Transition::Ready;
                eprintln!("无法卸载 Oopz 网页：{error}");
            }
        }
        (false, None) => {}
    }
}

fn finish_open(app: &AppHandle, result: tauri::Result<tauri::WebviewWindow>) {
    let transition = mem::take(&mut *app.state::<Session>().0.lock().unwrap());
    match result {
        Ok(_) if matches!(transition, Transition::Opening { unload_after: true }) => {
            request(app, false);
        }
        Ok(_) => PageMenuItems::set_enabled(app, true),
        Err(error) => {
            use windows_sys::{
                core::w,
                Win32::UI::WindowsAndMessaging::{MessageBoxW, MB_ICONERROR, MB_OK},
            };
            eprintln!("无法打开 Oopz 网页：{error}");
            unsafe {
                MessageBoxW(
                    std::ptr::null_mut(),
                    w!("无法创建 Oopz 窗口。请检查 Microsoft Edge WebView2 Runtime 后重新打开。"),
                    w!("Oopz"),
                    MB_ICONERROR | MB_OK,
                );
            }
            if app.tray_by_id(TRAY_ID).is_none() {
                app.exit(1);
            }
        }
    }
}

pub(crate) fn on_run_event(app: &AppHandle, event: RunEvent) {
    match event {
        RunEvent::ExitRequested {
            code: None, api, ..
        } if app.tray_by_id(TRAY_ID).is_some() => {
            // The user can still explicitly exit from the tray (exit code Some(0)).
            api.prevent_exit();
        }
        RunEvent::WindowEvent {
            label,
            event: WindowEvent::Destroyed,
            ..
        } if label == MAIN_WINDOW => {
            PageMenuItems::set_enabled(app, false);
            let state = app.state::<Session>();
            let mut transition = state.0.lock().unwrap();
            if let Transition::Closing { reopen_after } = *transition {
                *transition = Transition::Ready;
                drop(transition);
                if reopen_after {
                    request(app, true);
                }
            }
        }
        _ => {}
    }
}
