using System.Collections.Generic;

namespace AwayLock;

public static class Strings
{
    private static readonly Dictionary<string, (string English, string Chinese)> Values = new()
    {
        ["tagline"] = ("Phone-away auto lock", "离身锁 · 手机离开自动锁屏"),
        ["heading"] = ("Lock when your phone leaves", "手机离开，电脑自动锁屏"),
        ["intro"] = ("Locks after your phone disconnects and your PC stays idle for 60 seconds. Keyboard or mouse input restarts confirmation.", "手机持续断开且电脑空闲满 60 秒后锁屏；操作键盘或鼠标会重新计时。"),
        ["appRunning"] = ("APP RUNNING", "程序已启动"),
        ["deviceTitle"] = ("YOUR PHONE", "选择手机"),
        ["deviceHint"] = ("Choose a paired Bluetooth device", "选择一个已配对的蓝牙设备"),
        ["refresh"] = ("Refresh", "刷新设备"),
        ["bluetoothSettings"] = ("Pair a phone in Windows ↗", "在 Windows 中配对手机 ↗"),
        ["start"] = ("Enable phone-away lock", "启用离身锁屏"),
        ["stop"] = ("Stop protection", "停止保护"),
        ["startup"] = ("✓ Protection continues after you exit the interface.", "✓ 退出界面及托盘后，后台保护持续生效。"),
        ["startupHint"] = ("Sign-in, unlock and a one-minute watchdog restore the separate background agent.", "独立守护进程在登录、解锁和每分钟看门狗触发时自动恢复。"),
        ["activity"] = ("RECENT ACTIVITY", "最近活动"),
        ["activityEmpty"] = ("Connection updates appear here after you select a phone.", "选择手机并启动后，这里会显示连接动态。"),
        ["checked"] = ("LAST CHECKED", "最近检查"),
        ["requests"] = ("SERVICE REQUESTS", "服务发现次数"),
        ["interval"] = ("CHECK / RETRY", "检查 / 重试间隔"),
        ["intervalValue"] = ("Events + 5–30s / 60s", "事件 + 5–30 秒 / 60 秒"),
        ["dynamicTitle"] = ("Windows keeps your sign-in protected", "锁屏后使用 Windows 登录验证"),
        ["dynamicBody"] = ("Use Windows Hello, PIN or password to sign in after locking. Windows Dynamic Lock is available in Sign-in options.", "锁屏后通过 Windows Hello、PIN 或密码登录。你也可以在 Windows 登录选项中配置动态锁。"),
        ["dynamicButton"] = ("Open Sign-in options ↗", "打开 Windows 登录选项 ↗"),
        ["footer"] = ("Windows 10 / 11 · Local preferences · v1.0.2", "Windows 10 / 11 · 本地保存偏好 · v1.0.2"),
        ["closeHint"] = ("Exit the interface; keep background protection", "退出界面，保持后台保护"),
        ["starting.title"] = ("Restoring background protection", "正在恢复后台保护"),
        ["starting.body"] = ("Windows is starting the independent agent. You can close this interface after protection starts.", "Windows 正在启动独立守护进程，保护启用后可以退出整个界面。"),
        ["choose.title"] = ("Ready to protect your session", "程序已启动，等待选择手机"),
        ["choose.body"] = ("Select a paired phone and enable phone-away lock.", "选择已经配对的手机，再启用离身锁屏。"),
        ["checking.title"] = ("Checking your phone", "正在检查手机连接"),
        ["checking.body"] = ("Querying Windows Bluetooth and refreshing device services.", "正在查询 Windows 蓝牙状态并请求设备服务。"),
        ["connected.title"] = ("Phone connected. Auto-lock armed.", "手机已连接，离身锁屏已就绪"),
        ["connected.body"] = ("Locks only after 60 seconds of confirmed disconnection and no keyboard or mouse input.", "仅在手机确认持续断开、且连续 60 秒没有键鼠操作时锁屏。"),
        ["active.title"] = ("PC in use · auto-lock postponed", "正在使用电脑，已暂缓锁屏"),
        ["active.body"] = ("Windows reports the phone disconnected, but recent keyboard or mouse input confirms you are here. Locking waits until the PC is idle for 60 seconds.", "Windows 报告手机断开，但检测到近期键鼠操作。连续空闲满 60 秒后才会锁屏，继续操作会重新计时。"),
        ["unknown.title"] = ("Connection check unavailable", "暂时无法确认连接状态"),
        ["unknown.body"] = ("Bluetooth or PC idle information is unavailable. Locking is paused until checks succeed and a fresh confirmation period completes.", "暂时无法读取蓝牙或电脑空闲状态，已暂停锁屏；检查恢复后会重新进行离开确认。"),
        ["waiting.title"] = ("Waiting for the first connection", "等待手机首次连接"),
        ["waiting.body"] = ("Connect the selected phone once to arm auto-lock. Reconnect attempts run in the background.", "手机成功连接一次后，离身锁屏才会就绪。程序正在后台尝试重连。"),
        ["confirming.title"] = ("Phone disconnected", "手机连接已断开"),
        ["confirming.body"] = ("Confirming your absence. Approximately {0}s before Windows locks.", "正在确认手机离开，约 {0} 秒后锁定 Windows。"),
        ["locking.title"] = ("Locking Windows", "正在锁定 Windows"),
        ["locking.body"] = ("The phone stayed disconnected and the PC received no keyboard or mouse input for the confirmation period.", "手机持续断开，且整个确认周期内电脑没有键鼠操作。"),
        ["locked.title"] = ("Windows session locked", "Windows 会话已锁定"),
        ["locked.body"] = ("Sign in through Windows to continue.", "通过 Windows 登录验证后继续使用。"),
        ["lockfailed.title"] = ("Windows lock request needs attention", "Windows 锁屏请求未完成"),
        ["lockfailed.body"] = ("Windows could not lock the interactive session. Another confirmation period will begin.", "Windows 未能锁定当前交互会话，程序将重新进行一次离开确认。"),
        ["timeout.title"] = ("Your phone needs more time", "等待手机响应"),
        ["timeout.body"] = ("The Bluetooth request timed out. Retrying in 60 seconds.", "本次蓝牙请求超时，60 秒后自动重试。"),
        ["pair.title"] = ("Pair this phone in Windows", "请先完成手机配对"),
        ["pair.body"] = ("Complete pairing in Bluetooth settings. AwayLock will check again.", "请在 Windows 蓝牙设置中完成配对，程序会继续检查。"),
        ["stopped.title"] = ("Protection stopped", "后台保护已停止"),
        ["stopped.body"] = ("Enable phone-away lock whenever you want to resume.", "需要恢复时，点击启用离身锁屏。"),
        ["show"] = ("Open AwayLock", "打开 AwayLock"),
        ["exit"] = ("Exit interface · protection continues", "退出界面 · 后台保护继续"),
        ["error"] = ("Please check this", "请检查以下信息"),
        ["deviceError"] = ("Device discovery failed. Check Bluetooth in Windows Settings, then refresh.", "读取设备失败，请检查 Windows 蓝牙设置后刷新设备。"),
        ["startupError"] = ("Windows could not update the startup task.", "Windows 无法更新自动启动任务。"),
        ["noDevices"] = ("Pair a phone in Windows, then refresh the device list.", "请先在 Windows 中配对手机，再刷新设备列表。")
    };

    public static string Get(string key, string language)
    {
        if (!Values.TryGetValue(key, out var value)) return key;
        return language == "zh-CN" ? value.Chinese : value.English;
    }
}
