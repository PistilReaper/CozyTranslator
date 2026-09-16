"""Build self-contained design references from actual WPF test renders."""
from pathlib import Path
import base64
import html

root = Path(__file__).resolve().parents[1]
qa = root / 'artifacts/qa'
out = root / '设计/v0.1-CozyTranslator'
out.mkdir(parents=True, exist_ok=True)

def image(name):
    return 'data:image/png;base64,' + base64.b64encode((qa / f'{name}.png').read_bytes()).decode()

style = '''
*{box-sizing:border-box}body{margin:0;background:#ece8df;color:#302f2b;font:14px/1.7 'Segoe UI','Microsoft YaHei',sans-serif}main{max-width:1360px;margin:auto;padding:64px 40px}header{display:flex;align-items:end;justify-content:space-between;gap:32px;border-bottom:1px solid #cec7ba;padding-bottom:28px;margin-bottom:44px}h1{font:52px/1.1 Georgia,serif;margin:8px 0 16px;letter-spacing:-2px}h2{font:26px Georgia,serif}h3{font-size:17px;font-weight:500;margin:0 0 6px}.eyebrow{font-size:11px;letter-spacing:2px;color:#a65c46}.muted{color:#79756e;font-size:12px}button{border:1px solid #d2cbbf;border-radius:24px;background:#f8f6f1;padding:10px 20px;color:#45423b;cursor:pointer}button.active{background:#353832;color:#f8f6f1}.grid{display:grid;grid-template-columns:1fr 1fr;gap:48px}.card img{width:100%;max-width:470px;display:block;margin:18px auto 4px}.card p{color:#79756e}footer{border-top:1px solid #cec7ba;margin-top:48px;padding-top:24px;font-size:12px;color:#79756e}.states{grid-template-columns:repeat(3,1fr);gap:36px}.states img{max-width:340px}.tag{display:inline-block;background:#e4d9c9;border-radius:20px;padding:3px 10px;font-size:11px;margin-bottom:12px}table{border-collapse:collapse;width:100%;font-size:13px}td,th{border-bottom:1px solid #d8d2c6;padding:14px 12px;text-align:left;vertical-align:top}nav{display:flex;flex-wrap:wrap;gap:8px;margin-bottom:36px}a{color:#a65c46;text-underline-offset:3px}@media(max-width:760px){main{padding:30px 18px}.grid,.states{grid-template-columns:1fr}h1{font-size:38px}header{display:block}.card img{max-width:430px}}
'''

def page(title, body, script=''):
    return f'<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{title}</title><style>{style}</style><main>{body}</main><script>{script}</script></html>'

body = '''<header><div><div class="eyebrow">COZYTRANSLATOR · DESIGN REFERENCE · 0.3.0-beta.1</div><h1>CozyTranslator</h1><div class="muted">苹果式的克制与留白，Anthropic 式的暖色与阅读感。</div></div><div><button id="light" class="active" onclick="theme(false)">暖纸白</button> <button id="dark" onclick="theme(true)">暖炭黑</button></div></header><div class="grid">'''
for label, desc, key in [('01 / Translation', '原文与译文上下对照。', 'translation'), ('02 / Dictionary', '单词、音标、释义与本地朗读。', 'dictionary')]:
    body += f'<section class="card"><h2>{label}</h2><p>{desc}</p><img id="{key}" alt="{desc}" src="{image(key+"-light")}" data-light="{image(key+"-light")}" data-dark="{image(key+"-dark")}"></section>'
body += '''</div><footer>图像来自实际 WPF 界面的测试渲染，示例译文和词条由测试夹具提供。它们不是云端模型的实测结果。<br>窗口、字体、图标、输入、下拉选择和滑杆均在桌面应用中真实实现。系统主题在实际应用中自动跟随。</footer>'''
script = "function theme(d){for(const id of ['translation','dictionary']){let el=document.getElementById(id);el.src=d?el.dataset.dark:el.dataset.light;}document.getElementById('light').classList.toggle('active',!d);document.getElementById('dark').classList.toggle('active',d);}"
(out/'主界面设计稿.html').write_text(page('CozyTranslator · 设计预览', body, script), encoding='utf-8')

states = [
    ('科研译文', 'Markdown、表格与本地公式排版。', 'markdown-light', 'normal'),
    ('科研译文 · 深色', '公式与正文同步跟随主题。', 'markdown-dark', 'normal'),
    ('正常翻译', '完整译文流式生成后保持可选取，可复制。', 'translation-light', 'normal'),
    ('深色主题', '暖炭黑表面、米白文字与陶土强调色。', 'translation-dark', 'normal'),
    ('英文查词', '自动识别单词，展开精简词典与本地朗读。', 'dictionary-light', 'normal'),
    ('等待响应', '连接及生成状态用文字说明；主操作可停止。', 'loading-light', 'feedback'),
    ('用户停止', '已生成部分保持可见，明确显示内容未完成。', 'stopped-light', 'feedback'),
    ('服务错误', '错误原因持续显示，允许用户手动重试。', 'error-light', 'feedback'),
    ('未识别词条', '保留查询词，说明未识别，停用朗读。', 'unknown-word', 'feedback'),
    ('长文本', '原文与译文各自滚动，操作按钮保持可见。', 'long-text', 'boundary'),
    ('最小窗口', '440 × 560 DIP，输入、结果与底部操作仍可用。', 'minimum-size', 'boundary'),
    ('设置', '主题、字号、触发模式、模型、提示词与快捷键。', 'settings-light', 'normal'),
]
body = '''<header><div><div class="eyebrow">COZYTRANSLATOR · STATES & INTERACTIONS</div><h1>界面与交互</h1><div class="muted">v0.3.0-beta.1 · 2026-09-16 · 实际 WPF 状态参考</div></div><a href="#rules">交互规则 ↓</a></header><nav><button onclick="filter('all')">全部</button><button onclick="filter('normal')">主界面</button><button onclick="filter('feedback')">反馈状态</button><button onclick="filter('boundary')">边界情况</button></nav><div class="grid states">'''
for title, desc, img, category in states:
    body += f'<section class="card" data-category="{category}"><span class="tag">{title}</span><p>{desc}</p><img loading="lazy" src="{image(img)}" alt="{title}"></section>'
body += '</div><section id="rules"><h2>Interaction rules</h2><table><tr><th>触发</th><th>系统行为</th><th>反馈</th></tr>'
rules = [
    ('左键保持按下 + 右键（模式 2）', '直接读取原应用选区，查词或翻译；不改剪贴板。', '显示结果并保持原文焦点，普通右键仍保留菜单。'),
    ('外部复制（模式 1）', '读取新文本、分类并查询；自身复制、其他模式或设置期间跳过。', '自动填入原文并显示译文或词典，不抢焦点。'),
    ('点击悬浮球／快捷键', '手动读取当前剪贴板并查询。', '唤出窗口并显示结果。'),
    ('相同剪贴板再次唤出', '保留当前会话；应用自己复制的结果不会自动反向翻译。', '显示已有结果。'),
    ('剪贴板为空／非文字／占用', '显示原因，并允许直接输入。', '没有文字／暂时占用的明确提示。'),
    ('手动编辑原文', '取消旧请求、清除旧结果；提交后查询。', '原文更新，结果回到待查询状态。'),
    ('模式或方向变化', '已有查询时发起新查询；旧请求取消。', '仅当前请求更新界面。'),
    ('滑杆落到新档位', '松手后发起一次重新翻译；同档不重复请求。', '新风格名称与新译文。'),
    ('复制', '复制译文或结构化词条文本。', '显示“已复制”；请求结束状态优先于复制提示。'),
    ('朗读', '调用安装的英文 SAPI 语音；重复点击替换播放。', '朗读提示；缺失语音或无权限时显示原因。'),
    ('停止／请求超时', '取消请求并保留已有部分。', '已停止未完成／请求超时。'),
    ('鉴权、配额、限流、接口错误', '结束当前请求并显示错误，等待手动重试。', '按错误类型给出可操作的原因。'),
    ('空返回／词条 JSON 无效／流中断', '校验失败，不将不完整响应标成成功。', '格式不正确、空内容或连接提前结束。'),
    ('启动／失焦', '默认显示主窗口及任务栏图标；失焦保持显示。', '图钉控制是否置顶。'),
    ('最小化／关闭／Esc', '最小化保留任务栏入口；关闭隐藏到托盘；Esc 显示悬浮球。', '保留当前会话，托盘可恢复主窗口。'),
    ('拖动／缩放／切换模式', '空白区域拖动和八向缩放；切换查词、翻译不重置尺寸。', '手动窗口尺寸得到保留。'),
    ('主题／字号', '跟随系统、浅色或深色；字号 16–24，保存后记忆。', '设置与主界面实时预览。'),
    ('保存设置', '验证配置及快捷键，DPAPI 加密后原子写入。', '成功关闭设置；失败显示原因。'),
    ('再次启动／托盘退出', '再次启动唤出现有实例；退出释放请求、语音、图标与快捷键。', '单实例运行。'),
]
for row in rules:
    body += '<tr>' + ''.join('<td>'+html.escape(cell)+'</td>' for cell in row) + '</tr>'
body += '</table><p class="muted">反馈显示在窗口底部，当前只显示一条。错误与未完成状态持续到下一次输入或查询；完整文案可通过悬停查看。没有搜索列表或多数据源聚合界面。</p></section><footer>示例内容来自测试夹具；正式应用使用用户配置的 LLM。真实云端凭据联调及实体多显示器缩放检查的范围见 VALIDATION.md。</footer>'
(out/'全状态设计参考.html').write_text(page('CozyTranslator · 状态参考', body, "function filter(c){for(const el of document.querySelectorAll('[data-category]'))el.hidden=c!=='all'&&el.dataset.category!==c;}"), encoding='utf-8')
print('Design references generated.')
