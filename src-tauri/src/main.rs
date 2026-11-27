#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

#[cfg(target_os = "windows")]
mod microphone_settings;
#[cfg(target_os = "windows")]
mod session;
#[cfg(target_os = "windows")]
mod tray_startup;
#[cfg(target_os = "windows")]
mod webview_memory;

use tauri::{
    menu::{Menu, MenuItem, Submenu},
    AppHandle, Manager, WebviewWindow, WebviewWindowBuilder, Wry,
};

const MAIN_WINDOW: &str = "main";
const RELOAD: &str = "reload";
const QUIT: &str = "quit";

fn restore(window: &WebviewWindow) {
    if window.show().is_ok() {
        if window.is_minimized().unwrap_or(false) {
            let _ = window.unminimize();
        }
        #[cfg(target_os = "windows")]
        webview_memory::foreground(window);
        let _ = window.set_focus();
    }
}

#[cfg(target_os = "windows")]
const TRAY_ID: &str = "oopz-tray";

#[cfg(target_os = "windows")]
struct PageMenuItems([MenuItem<Wry>; 3]);

#[cfg(target_os = "windows")]
impl PageMenuItems {
    fn set_enabled(app: &AppHandle, enabled: bool) {
        if let Some(items) = app.try_state::<Self>() {
            for item in &items.0 {
                let _ = item.set_enabled(enabled);
            }
        }
    }
}

#[cfg(target_os = "windows")]
fn setup_tray(app: &AppHandle) -> tauri::Result<bool> {
    use tauri::tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent};

    let Some(icon) = app.default_window_icon() else {
        return Ok(false);
    };
    let show = MenuItem::with_id(app, "show", "打开 Oopz", true, None::<&str>)?;
    let hide = MenuItem::with_id(app, "hide", "隐藏到托盘", false, None::<&str>)?;
    let reload = MenuItem::with_id(app, RELOAD, "重新加载", false, None::<&str>)?;
    let quit = MenuItem::with_id(app, QUIT, "退出 Oopz", true, None::<&str>)?;
    let offline = MenuItem::with_id(
        app,
        "unload-page",
        "断开会话并卸载网页",
        false,
        None::<&str>,
    )?;
    let startup = tray_startup::build_item(app)?;
    let sound = Submenu::new(app, "系统声音", true)?;
    microphone_settings::append_to(app, &sound)?;
    let menu = Menu::with_items(
        app,
        &[&show, &hide, &reload, &offline, &sound, &startup, &quit],
    )?;

    TrayIconBuilder::with_id(TRAY_ID)
        .icon(icon.clone())
        .tooltip("Oopz")
        .menu(&menu)
        .show_menu_on_left_click(false)
        .on_menu_event(move |app, event| {
            if event.id().as_ref() == tray_startup::MENU_ID {
                tray_startup::handle_toggle(app, &startup);
            }
        })
        .on_tray_icon_event(|tray, event| {
            if let TrayIconEvent::Click {
                button: MouseButton::Left,
                button_state: MouseButtonState::Up,
                ..
            } = event
            {
                let app = tray.app_handle();
                if let Some(window) = app.get_webview_window(MAIN_WINDOW) {
                    if matches!(window.is_visible(), Ok(true))
                        && matches!(window.is_minimized(), Ok(false))
                    {
                        let _ = webview_memory::hide_to_tray(&window);
                        return;
                    }
                }
                session::open(app);
            }
        })
        .build(app)?;
    app.manage(PageMenuItems([hide, reload, offline]));
    Ok(true)
}

fn build_menu(app: &AppHandle) -> tauri::Result<Menu<Wry>> {
    let reload = MenuItem::with_id(app, RELOAD, "重新加载", true, Some("CmdOrCtrl+R"))?;
    let quit = MenuItem::with_id(app, QUIT, "退出 Oopz", true, Some("CmdOrCtrl+Q"))?;
    let app_menu = Submenu::with_items(app, "Oopz", true, &[&reload, &quit])?;
    #[cfg(target_os = "windows")]
    {
        let unload =
            MenuItem::with_id(app, "unload-page", "断开会话并卸载网页", true, None::<&str>)?;
        app_menu.insert(&unload, 1)?;
    }

    let choose = MenuItem::with_id(app, "choose-image", "选择要隐藏的图片…", true, None::<&str>)?;
    let reset = MenuItem::with_id(app, "reset-images", "恢复已隐藏的图片", true, None::<&str>)?;
    let page = Submenu::with_items(app, "页面", true, &[&choose, &reset])?;

    let test = MenuItem::with_id(app, "mic-test", "麦克风自检…", true, None::<&str>)?;
    let cleanup = MenuItem::with_id(
        app,
        "voice-cleanup",
        "切换降噪与回声消除",
        true,
        None::<&str>,
    )?;
    let audio = Submenu::with_items(app, "语音", true, &[&test, &cleanup])?;
    #[cfg(target_os = "windows")]
    microphone_settings::append_to(app, &audio)?;
    Menu::with_items(app, &[&app_menu, &page, &audio])
}

fn run_on_oopz(window: &WebviewWindow, script: &str) {
    // Check at evaluation time too: navigation may race a native menu action.
    let scoped = format!(
        "if (window.top === window && location.origin === 'https://web.oopz.cn') {{ {script} }}"
    );
    if let Err(error) = window.eval(scoped) {
        eprintln!("无法执行 Oopz 页面操作：{error}");
    }
}

fn create_main_window(app: &AppHandle) -> tauri::Result<WebviewWindow> {
    let config = app
        .config()
        .app
        .windows
        .iter()
        .find(|window| window.label == MAIN_WINDOW)
        .ok_or(tauri::Error::WindowNotFound)?;
    WebviewWindowBuilder::from_config(app, config)?
        .initialization_script(include_str!("../web/focus.js"))
        .initialization_script(include_str!("../web/voice.js"))
        .build()
}

#[cfg(target_os = "windows")]
fn tray_only_arg<S: AsRef<std::ffi::OsStr>>(args: impl IntoIterator<Item = S>) -> bool {
    args.into_iter()
        .skip(1)
        .any(|arg| arg.as_ref() == "--tray-only")
}

fn main() {
    let builder = tauri::Builder::default();
    #[cfg(target_os = "windows")]
    let builder = builder.manage(session::Session::default());
    let builder = builder
        .plugin(tauri_plugin_single_instance::init(|app, _args, _| {
            #[cfg(target_os = "windows")]
            if !tray_only_arg(_args) {
                session::open(app);
            }
            #[cfg(not(target_os = "windows"))]
            if let Some(window) = app.get_webview_window(MAIN_WINDOW) {
                restore(&window);
            }
        }))
        .menu(build_menu)
        .setup(|app| {
            #[cfg(target_os = "windows")]
            {
                let tray_ready = setup_tray(app.handle()).unwrap_or_else(|error| {
                    eprintln!("无法初始化 Oopz 托盘：{error}");
                    false
                });
                if !tray_ready
                    || !(tray_only_arg(std::env::args_os())
                        || tray_startup::preferred(app.handle()))
                {
                    session::open(app.handle());
                }
            }
            #[cfg(not(target_os = "windows"))]
            create_main_window(app.handle())?;
            Ok(())
        })
        .on_menu_event(|app, event| {
            if event.id().as_ref() == QUIT {
                app.exit(0);
                return;
            }

            #[cfg(target_os = "windows")]
            match event.id().as_ref() {
                "show" => return session::open(app),
                "unload-page" => return session::unload(app),
                id if microphone_settings::handle(id) => return,
                _ => {}
            }
            if let Some(window) = app.get_webview_window(MAIN_WINDOW) {
                match event.id().as_ref() {
                    "choose-image" => run_on_oopz(&window, "window.__oopzDesktopImages?.choose()"),
                    "reset-images" => run_on_oopz(&window, "window.__oopzDesktopImages?.reset()"),
                    "voice-cleanup" => run_on_oopz(&window, "window.__oopzDesktopVoice?.toggle()"),
                    "mic-test" => run_on_oopz(&window, include_str!("../web/microphone-test.js")),
                    RELOAD => {
                        let _ = window.reload();
                    }
                    #[cfg(target_os = "windows")]
                    "hide" => {
                        let _ = webview_memory::hide_to_tray(&window);
                    }
                    _ => {}
                }
            }
        });

    #[cfg(target_os = "windows")]
    let builder = builder.on_window_event(|window, event| {
        if let tauri::WindowEvent::CloseRequested { api, .. } = event {
            if window.label() == MAIN_WINDOW
                && window.app_handle().tray_by_id(TRAY_ID).is_some()
                && window
                    .get_webview_window(MAIN_WINDOW)
                    .is_some_and(|main| webview_memory::hide_to_tray(&main).is_ok())
            {
                api.prevent_close();
            }
        }
    });

    let app = builder
        .build(tauri::generate_context!())
        .expect("无法启动 Oopz");
    #[cfg(target_os = "windows")]
    app.run(session::on_run_event);
    #[cfg(not(target_os = "windows"))]
    app.run(|_, _| {});
}
