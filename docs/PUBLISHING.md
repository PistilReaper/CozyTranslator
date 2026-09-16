# 发布步骤

1. 修改 `Directory.Build.props` 中的版本号；公测版本使用 `-beta.N` 后缀。
2. 更新 README、验证记录和对应的 Release 说明。
3. 在正常 Windows 桌面运行 `./build.ps1`，执行核心和桌面测试并打包。
4. 运行 `python tools/generate-design.py` 和 `python tools/package-source.py`；验收实际发布程序的窗口与主题交互。
5. 检查待提交文件：仅本项目源码、测试和公开文档；不提交 `.tools`、`artifacts`、个人设置或密钥。
6. 提交代码，创建版本标签，推送到 GitHub。
7. 用 GitHub CLI 创建预发布，上传便携包、源码包和校验文件。公测版保留 **Pre-release** 标记。

首次公测的命令示例（已安装 GitHub CLI 并登录后）：

```powershell
git tag -a v0.3.0-beta.1 -m 'CozyTranslator v0.3.0-beta.1 public beta'
git push origin main --follow-tags
gh release create v0.3.0-beta.1 --verify-tag --prerelease --draft --title 'CozyTranslator v0.3.0-beta.1 · 首次公测' --notes-file docs/RELEASE-v0.3.0-beta.1.md artifacts/CozyTranslator-v0.3.0-beta.1-win-x64.zip artifacts/CozyTranslator-v0.3.0-beta.1-source.zip artifacts/SHA256SUMS-v0.3.0-beta.1.txt
gh release view v0.3.0-beta.1
gh release edit v0.3.0-beta.1 --draft=false --prerelease
```

源码使用 MIT；第三方库和字体许可证位于 `THIRD-PARTY.md`。
