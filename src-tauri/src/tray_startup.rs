use std::{fs, io, path::Path};

use tauri::{menu::CheckMenuItem, AppHandle, Manager, Wry};
use windows_sys::{
    core::w,
    Win32::UI::WindowsAndMessaging::{MessageBoxW, MB_ICONERROR, MB_OK},
};

pub const MENU_ID: &str = "start-in-tray";
const FLAG: &str = "start-in-tray.enabled";

fn read_flag(path: &Path) -> io::Result<bool> {
    match fs::symlink_metadata(path) {
        Ok(meta) if meta.is_file() && meta.len() == 0 => Ok(true),
        Ok(_) => Err(io::Error::new(
            io::ErrorKind::InvalidData,
            "托盘启动设置文件格式无效",
        )),
        Err(error) if error.kind() == io::ErrorKind::NotFound => Ok(false),
        Err(error) => Err(error),
    }
}

pub fn preferred(app: &AppHandle) -> bool {
    app.path()
        .app_config_dir()
        .ok()
        .and_then(|dir| read_flag(&dir.join(FLAG)).ok())
        .unwrap_or(false)
}

pub fn build_item(app: &AppHandle) -> tauri::Result<CheckMenuItem<Wry>> {
    CheckMenuItem::with_id(
        app,
        MENU_ID,
        "启动时仅显示托盘图标",
        true,
        preferred(app),
        None::<&str>,
    )
}

fn save(dir: &Path, enabled: bool) -> io::Result<()> {
    let path = dir.join(FLAG);
    if read_flag(&path)? == enabled {
        return Ok(());
    }
    if enabled {
        fs::create_dir_all(dir)?;
        fs::File::create_new(path)?;
    } else {
        fs::remove_file(path)?;
    }
    Ok(())
}

pub fn handle_toggle(app: &AppHandle, item: &CheckMenuItem<Wry>) {
    // The native menu updates its check state before dispatching a click.
    let result = item.is_checked().and_then(|enabled| {
        let dir = app.path().app_config_dir()?;
        save(&dir, enabled).map_err(tauri::Error::from)
    });
    if let Err(error) = result {
        let _ = item.set_checked(preferred(app));
        eprintln!("无法保存 Oopz 托盘启动设置：{error}");
        // Both strings are static, NUL-terminated UTF-16.
        unsafe {
            MessageBoxW(
                std::ptr::null_mut(),
                w!("无法保存托盘启动设置。请检查应用配置目录的访问权限。"),
                w!("Oopz"),
                MB_OK | MB_ICONERROR,
            );
        }
    }
}
