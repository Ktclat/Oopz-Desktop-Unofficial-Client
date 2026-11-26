use std::ptr::null;

use tauri::{
    menu::{MenuItem, Submenu},
    AppHandle, Wry,
};
use windows_sys::{
    core::w,
    Win32::UI::{
        Shell::ShellExecuteW,
        WindowsAndMessaging::{MessageBoxW, MB_ICONERROR, MB_OK, SW_SHOWNORMAL},
    },
};

const SOUND: &str = "windows-sound-settings";
const PRIVACY: &str = "windows-microphone-privacy";

pub fn append_to(app: &AppHandle, menu: &Submenu<Wry>) -> tauri::Result<()> {
    let sound = MenuItem::with_id(app, SOUND, "系统声音设置…", true, None::<&str>)?;
    let privacy = MenuItem::with_id(app, PRIVACY, "系统麦克风权限…", true, None::<&str>)?;
    menu.append_items(&[&sound, &privacy])
}

pub fn handle(id: &str) -> bool {
    let uri = match id {
        SOUND => w!("ms-settings:sound"),
        PRIVACY => w!("ms-settings:privacy-microphone"),
        _ => return false,
    };

    // The URIs and strings are fixed, NUL-terminated UTF-16 literals.
    unsafe {
        let status = ShellExecuteW(
            std::ptr::null_mut(),
            w!("open"),
            uri,
            null(),
            null(),
            SW_SHOWNORMAL,
        );
        if status as isize <= 32 {
            MessageBoxW(
                std::ptr::null_mut(),
                w!("无法打开 Windows 设置。请从开始菜单打开“设置”，搜索“声音”或“麦克风隐私设置”。"),
                w!("Oopz"),
                MB_OK | MB_ICONERROR,
            );
        }
    }
    true
}
