# clash:// 一键导入 / Quick import

安装器的“处理 clash:// 订阅链接”组件注册当前用户的 URL 协议，不需要管理员权限。若另一个应用已经处理此协议，该组件默认不勾选；主动勾选才会替换。卸载仅在协议仍指向此安装时移除关联，保留之后由其他应用接管的关联。

支持的标准格式：

```text
clash://install-config?url=https%3A%2F%2Fexample.com%2Fsubscription%3Ftoken%3DEXAMPLE&name=My%20profile
clash://install-config/?name=My%20profile&url=https%3A%2F%2Fexample.com%2Fsubscription
```

`url` 必填，`name` 可省略；省略时以订阅站点主机名作为建议名称。订阅必须返回 Clash / Mihomo YAML，不能用文件地址、JavaScript 或一个原始 VLESS / VMess 单节点替代。

点击链接会打开或唤起 Cute Clash，并展示带名称、遮蔽地址和来源主机的确认窗口。点击“添加”后才下载。取消不会保存、联网下载或连接代理；导入成功也不会自动连接。已有实例通过仅限同一 Windows 用户的命名管道接收，不通过开放的本地 TCP 端口，错误提示不包含订阅 token。

编码规则：

- 推荐使用 `encodeURIComponent(subscriptionUrl)` 编码完整 URL，名称同样采用 UTF-8 百分号编码。
- 支持参数顺序变化和 `install-config/` 末尾斜杠。
- 百分号编码只还原一层；URL 内已有的 `%26`、`%2B`、签名参数和字面 `+` 保持原值。
- 为兼容已有机场链接，直接写 `url=https://…` 时，后续非保留的 `&key=value` 作为订阅自身 query 保留。`url` / `name` 是外层保留键；如果订阅自己的 query 使用这两个名字，请编码完整 URL 来消除歧义。
- 拒绝重复的 `url` / `name`、未知操作、控制字符、无效编码和带用户名密码的 URL。已编码 URL 后出现未知外层参数也会拒绝，避免静默丢失数据。

便携包可直接用命令行传入链接，但不会自动修改系统协议关联：

```powershell
.\cute-clash.exe 'clash://install-config?url=https%3A%2F%2Fexample.com%2Fsubscription&name=Example'
```

For English users: select **Handle clash:// subscription links** during setup. Existing handlers are preserved by default. Links open a prefilled confirmation dialog; only **Add** downloads a YAML subscription, and import never auto-connects. The same format works while Cute Clash is already running. Percent-encode the full subscription URL and name; user profile names, token bytes and literal plus signs are preserved.
