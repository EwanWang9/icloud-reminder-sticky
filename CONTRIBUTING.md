# 参与开发

欢迎提交 Issue 和 Pull Request。

## 本地构建

需要 Windows 10/11、.NET 8 SDK 和 Microsoft Edge WebView2 Runtime。

```powershell
dotnet restore
dotnet build -c Release
```

生成可分发包：

```powershell
.\build-release.ps1 -Version 0.1.0
```

## 发布新版本

1. 更新 `iCloudReminderSticky.csproj` 中的 `Version`、`AssemblyVersion` 和 `FileVersion`。
2. 在 `CHANGELOG.md` 添加版本说明。
3. 提交并推送到 `main`，确认 CI 通过。
4. 创建并推送标签，例如 `git tag v0.2.0` 和 `git push origin v0.2.0`。
5. GitHub Actions 会自动构建 x64、ARM64 安装包并创建 Release。

## iCloud 网页兼容性

网页选择器集中在 `MainWindow.xaml.cs`。修改完成与读取逻辑时，请同时测试：

- 普通标题和重复标题；
- 从 iPhone/iPad/Mac 新增及完成项目；
- iCloud 登录过期后的网页回退流程；
- Apple 网页更新后的 `button.mark-completed` 与添加按钮属性。
