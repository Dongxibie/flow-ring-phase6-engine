> **本仓库是 [Flow Ring](https://github.com/Dongxibie/flow-ring-phase7-package) 的分阶段开发记录（第 6 步 · 动作引擎与档案存储）。**
> 完整产品（源码 / 截图 / 下载即用的 Windows 安装包）在 **[flow-ring-phase7-package](https://github.com/Dongxibie/flow-ring-phase7-package)**，建议从产品仓库开始了解本项目。

---

# Flow Ring · 第 6 步：动作引擎与档案存储

Flow Ring 是一个 Windows 桌面快捷环：按住鼠标侧键唤出悬浮圆环，把光标拖向某个方向后松开，即可执行常用动作，不用离开当前窗口。项目按开发阶段拆分为 7 个仓库，本仓库是其中的第 6 步。

## 本步骤完成内容

- ActionEngine Pipeline：ActionPipeline（PermissionCheck + ContextInject + AuditLog）
- IActionRegistry + MemoryActionRegistry
- IProfileStore + FileSystemProfileStore（写 tmp → `File.Replace` 原子写，快照保留 5 个）
- AesGcmFlowCodeCodec（AES-256-GCM + PBKDF2-SHA256 200k 次）

## 验证结果（阶段记录）

- 60 个测试全绿
