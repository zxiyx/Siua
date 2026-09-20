using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Siua.Core.Xxt;

/// <summary>封装学习通章节测试的题目加载与提交操作。</summary>
public sealed class XxtChapterTest
{
    private readonly ILocator _container;
    private readonly List<XxtQuestion> _questions = [];
    private ILocator? _testPanel;
    private IFrame? _testFrame;

    public XxtChapterTest(ILocator container)
    {
        _container = container;
    }

    public IReadOnlyList<XxtQuestion> Questions => _questions;
    public bool IsCompleted { get; private set; }
    public bool HasQuestion => _questions.Count > 0;

    public Task<byte[]> CaptureRegionAsync(CancellationToken cancellationToken = default)
    {
        if (_testFrame is null || _testPanel is null)
            throw new PlaywrightException("章节测试尚未加载，无法截图。");
        return ChapterRegionCapture.CaptureAsync(_testFrame, _testPanel.Locator("form #ZyBottom"), cancellationToken);
    }

    public async Task SubmitAnswerAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_testPanel is null)
        {
            throw new PlaywrightException("学习通章节测试尚未加载，无法提交。");
        }
        var submitContainer = _testPanel.Locator("div.ZY_sub.clearfix");
        if (await submitContainer.CountAsync() == 0 || !await submitContainer.IsVisibleAsync())
        {
            throw new PlaywrightException("学习通章节测试没有找到可见的提交区域。");
        }
        var submitButton = submitContainer.Locator("a.btnSubmit").First;
        if (await submitButton.CountAsync() == 0 || !await submitButton.IsVisibleAsync())
        {
            throw new PlaywrightException("学习通章节测试没有找到可见的提交按钮。");
        }
        await submitButton.ClickAsync();
    }

    public async Task LoadQuestionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _questions.Clear();
        var frame = await GetRequiredContentFrameAsync(
            _container.Locator("iframe").First,
            cancellationToken);
        var innerFrame = await GetRequiredContentFrameAsync(
            frame.Locator("iframe").First,
            cancellationToken);
        _testFrame = innerFrame;

        IsCompleted = await innerFrame.Locator("div.testTit_status_complete").CountAsync() > 0;
        _testPanel = innerFrame.Locator("div.radiusBG > div.CeYan");
        if (IsCompleted)
        {
            return;
        }

        var questionsContainer = _testPanel.Locator("form #ZyBottom");
        await questionsContainer.Locator("div.TiMu.newTiMu").First.WaitForAsync();
        var questionLocators = questionsContainer.Locator("div.TiMu.newTiMu");
        var count = await questionLocators.CountAsync();
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _questions.Add(new XxtQuestion(questionLocators.Nth(index)));
        }
    }

    public async Task WaitForSubmissionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_testFrame is null)
        {
            throw new PlaywrightException("学习通章节测试尚未加载，无法确认提交结果。");
        }

        await _testFrame.Locator("div.testTit_status_complete").First.WaitForAsync();
        cancellationToken.ThrowIfCancellationRequested();
        IsCompleted = true;
    }

    private static async Task<IFrame> GetRequiredContentFrameAsync(
        ILocator locator,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await locator.WaitForAsync();
        var element = await locator.ElementHandleAsync()
            ?? throw new PlaywrightException("无法获取测试 iframe 元素。");
        var frame = await element.ContentFrameAsync()
            ?? throw new PlaywrightException("无法进入测试 iframe。");
        await XxtPageResolver.WaitForFrameContentAsync(frame, locator);
        return frame;
    }
}

/// <summary>表示一道学习通题目及其选择项或填空控件。</summary>
public sealed class XxtQuestion
{
    private readonly ILocator _container;
    private readonly List<XxtAnswerOption> _answers = [];
    private readonly List<BlankInput> _blanks = [];

    private sealed record BlankInput(
        ILocator Input,
        bool RequiresEditor,
        string? FieldId = null,
        string? MirrorId = null);

    public XxtQuestion(ILocator container)
    {
        _container = container;
    }

    public string Title { get; private set; } = string.Empty;
    public string Number { get; private set; } = string.Empty;
    public bool AllowsMultipleAnswers { get; private set; }
    public string? QuestionType { get; private set; }
    public bool IsFillInBlank { get; private set; }
    public int BlankCount => _blanks.Count;
    public IReadOnlyList<XxtAnswerOption> Answers => _answers;

    public async Task LoadAnswersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _answers.Clear();
        _blanks.Clear();
        AllowsMultipleAnswers = false;
        IsFillInBlank = false;
        var titleLocator = _container.Locator("div.Zy_TItle.clearfix").First;
        await titleLocator.WaitForAsync();
        Title = await titleLocator.InnerTextAsync();
        var number = titleLocator.Locator("i").First;
        Number = await number.CountAsync() > 0 ? (await number.InnerTextAsync()).Trim() : string.Empty;

        // 学习通题型 2 为填空题。题型字段缺失时再使用题干中的题型提示，
        // 避免把问答题、编辑器工具栏中的输入框当作填空。
        var questionType = _container.Locator(
            "input[type=hidden][name^=type i], input[type=hidden][id^=type i], " +
            "input[type=hidden][name^=answertype i], input[type=hidden][id^=answertype i], " +
            "input[type=hidden][name=questionType i]").First;
        var type = await questionType.CountAsync() > 0
            ? await questionType.GetAttributeAsync("value")
            : await _container.GetAttributeAsync("data");
        QuestionType = type;
        IsFillInBlank = type == "2" ||
            (string.IsNullOrWhiteSpace(type) && Title.Contains("填空题", StringComparison.Ordinal));
        if (IsFillInBlank)
        {
            await LoadBlanksAsync(cancellationToken);
            return;
        }

        // 问答/其它题也有 UEditor，不能把它们的工具栏或文本框当作选项或填空。
        if (!string.IsNullOrWhiteSpace(type) && type is not ("0" or "1" or "3"))
            return;

        AllowsMultipleAnswers = type == "1" || Title.Contains("多选题", StringComparison.Ordinal);

        var answerItems = _container.Locator("li");
        var count = await answerItems.CountAsync();
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = answerItems.Nth(index);
            var label = item.Locator("label").First;
            var answer = item.Locator("a").First;
            if (await label.CountAsync() == 0 || await answer.CountAsync() == 0)
            {
                continue;
            }
            AllowsMultipleAnswers |= await item.Locator("input[type=checkbox]").CountAsync() > 0 ||
                                     await item.GetAttributeAsync("qtype") == "1" ||
                                     await item.GetAttributeAsync("role") == "checkbox";
            var marker = item.Locator(".num_option").First;
            var labelText = (await label.InnerTextAsync()).Trim();
            var answerText = (await answer.InnerTextAsync()).Trim();
            if (await marker.CountAsync() > 0)
            {
                _answers.Add(new XxtAnswerOption(label, (await marker.InnerTextAsync()).Trim(), answerText));
            }
            else if (labelText.Length == 1 && labelText[0] is >= 'A' and <= 'H')
            {
                _answers.Add(new XxtAnswerOption(label, labelText, answerText));
            }
            else
            {
                // 兼容旧布局：label 是选项内容、a 是选项字母。
                _answers.Add(new XxtAnswerOption(label, answerText, labelText));
            }
        }
    }

    private async Task LoadBlanksAsync(CancellationToken cancellationToken)
    {
        var size = _container.Locator("input[type=hidden][name^=tiankongsize]").First;
        int? expectedCount = null;
        if (await size.CountAsync() > 0)
        {
            if (!int.TryParse(await size.GetAttributeAsync("value"), out var count) || count <= 0)
                throw new PlaywrightException("填空题的空格数量字段无效，停止填写。");
            expectedCount = count;
        }

        var blankItems = _container.Locator(".Zy_ulTk .blankItemDiv");
        if (await blankItems.CountAsync() > 0 ||
            (expectedCount.HasValue && await _container.Locator(".Zy_ulTk").CountAsync() > 0))
        {
            if (expectedCount.HasValue)
                await blankItems.Nth(expectedCount.Value - 1).WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Attached
                });

            var count = await blankItems.CountAsync();
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = blankItems.Nth(index);
                var field = item.Locator("textarea[id^=answerEditor]").First;
                var mirror = item.Locator(".InpDIV").First;
                if (await field.CountAsync() > 0)
                {
                    var editorFrame = item.Locator(".edui-editor-iframeholder iframe, iframe[id^=ueditor_]").First;
                    // 未激活的编辑器可能先显示答案预览，点击该空后才创建/显示 iframe。
                    if (!await editorFrame.IsVisibleAsync() && await mirror.IsVisibleAsync())
                        await mirror.ClickAsync();
                    await editorFrame.WaitForAsync();
                    await AddEditorBlankAsync(
                        editorFrame,
                        await field.GetAttributeAsync("id"),
                        await mirror.CountAsync() > 0 ? await mirror.GetAttributeAsync("id") : null,
                        cancellationToken);
                }
                else
                {
                    var before = _blanks.Count;
                    await LoadBlankControlsAsync(item, cancellationToken);
                    if (_blanks.Count != before + 1)
                        throw new PlaywrightException($"填空题第 {index + 1} 个空没有唯一的可填写控件。");
                }
            }
        }
        else
        {
            await LoadBlankControlsAsync(_container, cancellationToken);
        }

        if (expectedCount.HasValue && expectedCount.Value != BlankCount)
            throw new PlaywrightException($"填空题声明 {expectedCount} 个空，但只识别到 {BlankCount} 个答题框，停止填写。");
    }

    private async Task LoadBlankControlsAsync(ILocator scope, CancellationToken cancellationToken)
    {
        // CSS 联合选择器保留 DOM 顺序；只枚举可见编辑区，隐藏的表单 textarea
        // 由 UEditor.sync() 回写，不再作为另一个空格计数。
        var controls = scope.Locator(
            "input[type=text], input:not([type]), textarea, " +
            "[contenteditable=true], [contenteditable=plaintext-only], " +
            ".edui-editor-iframeholder iframe, iframe[id^=ueditor_]");
        var count = await controls.CountAsync();
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var control = controls.Nth(index);
            if (!await control.IsVisibleAsync() || !await control.EvaluateAsync<bool>("""
                element => !element.disabled && !element.readOnly &&
                    !element.closest('.Zy_TItle, .edui-toolbar, .edui-editor-toolbarbox, .edui-popup, .edui-dialog, .latex-inline-pop') &&
                    !element.parentElement?.closest('[contenteditable=true], [contenteditable=plaintext-only]')
                """))
            {
                continue;
            }

            var isFrame = await control.EvaluateAsync<bool>("element => element.tagName === 'IFRAME'");
            if (isFrame)
            {
                await AddEditorBlankAsync(control, null, null, cancellationToken);
            }
            else
            {
                var isEditor = await control.EvaluateAsync<bool>(
                    "element => element.isContentEditable && !!element.closest('.edui-editor')");
                _blanks.Add(new BlankInput(control, isEditor));
            }
        }
    }

    private async Task AddEditorBlankAsync(
        ILocator iframe,
        string? fieldId,
        string? mirrorId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var handle = await iframe.ElementHandleAsync();
        var frame = handle is null ? null : await handle.ContentFrameAsync();
        if (frame is null)
            throw new PlaywrightException("无法进入学习通填空题的富文本编辑器。");

        // 真实 iframe 初始只有 body.view，UE._setup(document) 完成后才能输入。
        await frame.WaitForFunctionAsync("""
            () => document.body?.isContentEditable &&
                Object.values(window.parent.UE?.instants || {}).some(editor =>
                    editor.body === document.body && editor.isReady !== false &&
                    typeof editor.sync === 'function')
            """);
        cancellationToken.ThrowIfCancellationRequested();
        _blanks.Add(new BlankInput(frame.Locator("body"), true, fieldId, mirrorId));
    }

    public async Task FillBlanksAsync(
        IReadOnlyList<string> answers,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsFillInBlank || BlankCount == 0 || answers.Count != BlankCount)
        {
            throw new PlaywrightException($"学习通填空题答案数量不匹配：识别到 {BlankCount} 个空，收到 {answers.Count} 个答案。");
        }
        for (var index = 0; index < answers.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(answers[index]))
            {
                throw new PlaywrightException($"学习通填空题第 {index + 1} 个答案为空，停止填写。");
            }
        }

        for (var index = 0; index < _blanks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var blank = _blanks[index];
            await blank.Input.FillAsync(answers[index]);
            // fill 会触发 input，但部分页面在 change/blur 或 UEditor contentchange
            // 时才保存答案。富文本必须同步并检查真实表单值，不能只改 iframe DOM。
            var error = await blank.Input.EvaluateAsync<string?>("""
                (element, args) => {
                    const view = element.ownerDocument.defaultView;
                    const emit = target => {
                        const EventType = target.ownerDocument.defaultView.Event;
                        for (const name of ['input', 'change', 'blur'])
                            target.dispatchEvent(new EventType(name, { bubbles: true }));
                    };
                    emit(element);
                    element.blur();
                    if (!element.isContentEditable) return null;

                    const views = [view];
                    try { if (view.parent !== view && view.parent.document) views.push(view.parent); }
                    catch { /* 跨域编辑器无法通过父页面访问实例。 */ }
                    const editor = views.flatMap(owner => Object.values(owner.UE?.instants || {}))
                        .find(item => item.body === element || item.body?.contains(element));
                    if (!editor)
                        return args.requiresEditor ? '没有找到填空编辑器的 UEditor 实例，无法同步表单。' : null;
                    if (typeof editor.sync !== 'function')
                        return '填空编辑器不支持同步表单。';
                    editor.fireEvent?.('contentChange');
                    editor.sync();

                    const form = editor.container?.closest('form') || editor.iframe?.closest('form');
                    const owner = editor.container?.ownerDocument || view.parent.document;
                    const field = args.fieldId ? owner.getElementById(args.fieldId)
                        : editor.textarea || (editor.key && form?.elements.namedItem(editor.key));
                    if (!field || typeof field.value !== 'string')
                        return '没有找到填空编辑器对应的表单字段。';
                    emit(field);
                    const normalize = text => text.replace(/\s+/g, ' ').trim();
                    const readText = html => {
                        const decoded = element.ownerDocument.createElement('div');
                        decoded.innerHTML = html;
                        // textContent 不保留 <br> 或段落边界；比较前还原换行。
                        for (const node of decoded.querySelectorAll('br, p, div, li')) {
                            if (node.tagName !== 'BR') node.before('\n');
                            node.after('\n');
                        }
                        return normalize(decoded.textContent || '');
                    };
                    if (readText(field.value) !== normalize(args.answer))
                        return '填空编辑器内容没有正确同步到表单字段。';
                    if (args.mirrorId) {
                        const mirror = owner.getElementById(args.mirrorId);
                        if (!mirror || readText(mirror.innerHTML) !== normalize(args.answer))
                            return '填空编辑器内容没有正确同步到答案预览字段。';
                    }
                    return null;
                }
                """, new
                {
                    answer = answers[index], requiresEditor = blank.RequiresEditor,
                    fieldId = blank.FieldId, mirrorId = blank.MirrorId
                });
            if (error is not null)
            {
                throw new PlaywrightException(error);
            }
        }
    }

    public async Task<byte[]?> CaptureImageAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            // 填空答案由题干和空数确定，编辑器工具栏及已有答案不应混入 OCR。
            var target = IsFillInBlank ? _container.Locator("div.Zy_TItle.clearfix").First : _container;
            return await target.ScreenshotAsync(new LocatorScreenshotOptions
            {
                Animations = ScreenshotAnimations.Disabled,
                Type = ScreenshotType.Png,
                Timeout = 5000
            });
        }
        catch (PlaywrightException)
        {
            return null;
        }
    }
}

public sealed record XxtAnswerOption(ILocator Target, string Marker, string Text);
