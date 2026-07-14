# Contributing to HubRoute

感谢参与 HubRoute。项目优先接受能够提高代理兼容性、跨平台可靠性、安全性和可验证性的改动。

## 开发环境

- .NET 10 SDK
- Windows、macOS 或 Linux
- 可选：Unity Hub，用于端到端启动验证

~~~powershell
dotnet restore HubRoute.slnx
dotnet test HubRoute.slnx
dotnet run --project src/HubRoute/HubRoute.csproj
~~~

## 提交要求

1. 修改范围与问题直接相关，不顺手重构无关模块。
2. 不提交 bin、obj、安装包、代理配置、日志或凭据。
3. 新增平台分支时，提供不依赖当前主机的参数化测试。
4. 涉及网络或进程启动时，不修改系统全局代理，不记录代理凭据。
5. 公开和私有方法使用简短 XML 注释说明职责与约束。
6. 提交前运行测试和 Release 构建，确保 0 警告。

## 拉取请求

拉取请求请说明：

- 解决的问题与用户场景
- 受影响的平台和架构
- 验证命令及结果
- 无法在本机验证的环境
- UI 变化截图（如适用）

安全漏洞不要提交公开 Issue，请遵循 [SECURITY.md](SECURITY.md)。
