# 非打包版计划任务自启动

非打包版继续使用 Windows 计划任务，不写入“启动”文件夹、Run 注册表项或 CLI 别名。

原生包的创建接口在没有 MSIX 身份时仍使用 WindowsApps 下的 `sh.exe` / `shdev.exe`，而非打包应用没有这些别名。`HutaoNative.CreateAutoStartTaskForThisUser` 现在仅在非打包模式走 `UnpackagedStartupTask`：生成任务 XML，通过系统目录中的 `schtasks.exe /Create /XML /F` 创建或更新任务；已打包模式继续调用原有接口。

- 沿用 `\Hutao\Autorun for <当前用户名>`，因此原生查询、权限读取及删除接口仍然适用。
- 登录触发器和执行主体均绑定当前用户 SID，使用 InteractiveToken，不存储密码。
- 任务直接执行当前 exe，并以其目录作为工作目录；普通权限和管理员权限分别使用 LeastPrivilege / HighestAvailable。
- XML 及 ArgumentList 分别处理路径中的空格、中文和 XML 特殊字符；不通过命令解释器拼接命令。
- `/F` 更新旧任务，不只重新启用已有的错误动作；进入设置页时，管理员进程会修复非打包版旧别名路径或移动程序后留下的旧路径。
- 保留修改计划任务需管理员权限的现有约束。后台执行注册/删除，注册进程十秒超时；OS 操作成功后才保存设置，失败时提示并恢复 UI，避免开关与注册状态脱节。

8 项 `UnpackagedStartupTaskTest` 直接链接生产实现，验证任务定义、权限、路径、参数、更新标志及临时 XML 文件成功/失败清理。整套测试 154 通过、2 失败；失败项为环境相关的游戏注册表读取与 HTTPS/TLS 测试。另以提取的生产 `UpdateStartupTaskAsync` 方法体和替代依赖，检查成功后保存、失败不保存、已打包版回滚及并发/权限拦截流程，均通过；这不替代真实 UI 测试。

Windows 计划任务 COM 接口在内存中解析生产代码生成的普通权限、管理员权限两种 XML，均通过，没有注册、删除或运行任何真实任务。尚未实测登录启动及 UI 开关。


参考：[Windows schtasks create](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/schtasks-create)、[Task Scheduler schema](https://learn.microsoft.com/en-us/windows/win32/taskschd/task-scheduler-schema)。
