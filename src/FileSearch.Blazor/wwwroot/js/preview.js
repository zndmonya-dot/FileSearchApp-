// プレビュー本文描画（WinMerge 風）とハイライト行ナビ。
// Blazor からは previewBegin → previewAppend* → previewFinish でチャンク受信（大容量 JS 相互運用対策）。
(function () {
    var PREVIEW_MAX_LINES_DEFAULT = 15000;
    var BATCH_SIZE = 250;
    var PREVIEW_LINE_HEIGHT = 20;

    var _pendingContent = '';
    var _pendingOptions = null;
    var _highlightLineIndex = -1;
    var _highlightLineNumbers = [];

    function escapeHtml(text) {
        if (!text) return '';
        return String(text)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function normalizeLineText(line) {
        if (!line) return '';
        return String(line).replace(/\r$/, '').replace(/^\uFEFF/, '');
    }

    function mergeRanges(ranges) {
        if (!ranges.length) return [];
        ranges.sort(function (a, b) { return a.start - b.start || b.end - a.end; });
        var merged = [ranges[0]];
        for (var i = 1; i < ranges.length; i++) {
            var prev = merged[merged.length - 1];
            var cur = ranges[i];
            if (cur.start <= prev.end) {
                prev.end = Math.max(prev.end, cur.end);
            } else {
                merged.push(cur);
            }
        }
        return merged;
    }

    function findTermRanges(line, terms) {
        var ranges = [];
        if (!line || !terms || !terms.length) return ranges;

        var lowerLine = line.toLowerCase();
        for (var i = 0; i < terms.length; i++) {
            var term = terms[i];
            if (!term) continue;
            var lowerTerm = String(term).toLowerCase();
            if (!lowerTerm.length) continue;

            var pos = 0;
            while (pos < line.length) {
                var idx = lowerLine.indexOf(lowerTerm, pos);
                if (idx < 0) break;
                ranges.push({ start: idx, end: idx + term.length });
                pos = idx + Math.max(1, term.length);
            }
        }
        return mergeRanges(ranges);
    }

    function highlightHtml(line, terms) {
        var normalized = normalizeLineText(line);
        if (!terms || !terms.length) return escapeHtml(normalized);

        var ranges = findTermRanges(normalized, terms);
        if (!ranges.length) return escapeHtml(normalized);

        var html = '';
        var last = 0;
        for (var i = 0; i < ranges.length; i++) {
            var r = ranges[i];
            if (r.start > last) {
                html += escapeHtml(normalized.slice(last, r.start));
            }
            html += '<mark>' + escapeHtml(normalized.slice(r.start, r.end)) + '</mark>';
            last = r.end;
        }
        if (last < normalized.length) {
            html += escapeHtml(normalized.slice(last));
        }
        return html;
    }

    function createLineElement(lineNum, lineText, hasMatch, terms, isPlain) {
        var row = document.createElement('div');
        row.className = 'code-line' + (hasMatch ? ' line-match' : '');
        row.setAttribute('data-line-num', String(lineNum));

        var num = document.createElement('span');
        num.className = 'line-number';
        num.textContent = String(lineNum);

        var normalized = normalizeLineText(lineText);
        var content = document.createElement('span');
        content.className = 'line-content' + (hasMatch ? ' highlight' : '');
        if (isPlain) {
            content.textContent = normalized;
        } else {
            content.innerHTML = highlightHtml(normalized, terms);
        }

        row.appendChild(num);
        row.appendChild(content);
        return row;
    }

    function getPreviewElement() {
        return document.getElementById('preview-code-view');
    }

    function renderContent(content, options) {
        options = options || {};
        return new Promise(function (resolve) {
            var el = getPreviewElement();
            if (!el) {
                resolve();
                return;
            }

            el.replaceChildren();

            var isError = !!options.isError;
            var terms = options.searchTerms || [];
            var matchSet = new Set(options.matchLineNumbers || []);
            var maxLines = options.maxLines || PREVIEW_MAX_LINES_DEFAULT;

            if (isError) {
                el.appendChild(createLineElement(1, content, false, terms, true));
                resolve();
                return;
            }

            if (!content) {
                resolve();
                return;
            }

            var lines = content.split('\n');
            var total = lines.length;
            var renderCount = Math.min(total, maxLines);
            var index = 0;

            function renderBatch() {
                var frag = document.createDocumentFragment();
                var end = Math.min(index + BATCH_SIZE, renderCount);
                for (; index < end; index++) {
                    var lineNum = index + 1;
                    var lineText = lines[index];
                    var hasMatch = matchSet.has(lineNum);
                    frag.appendChild(createLineElement(lineNum, lineText, hasMatch, terms, false));
                }
                el.appendChild(frag);

                if (index < renderCount) {
                    requestAnimationFrame(renderBatch);
                } else {
                    if (renderCount < total && options.tooManyLinesMessage) {
                        el.appendChild(createLineElement(renderCount + 1, options.tooManyLinesMessage, false, [], true));
                    }
                    resolve();
                }
            }

            requestAnimationFrame(renderBatch);
        });
    }

    window.previewClear = function () {
        _pendingContent = '';
        _pendingOptions = null;
        var el = getPreviewElement();
        if (el) el.replaceChildren();
    };

    window.previewBegin = function (options) {
        _pendingOptions = options || {};
        _pendingContent = '';
        var el = getPreviewElement();
        if (el) el.replaceChildren();
    };

    window.previewAppend = function (chunk) {
        if (chunk) _pendingContent += chunk;
    };

    window.previewFinish = function () {
        var content = _pendingContent;
        var options = _pendingOptions || {};
        _pendingContent = '';
        _pendingOptions = null;
        return renderContent(content, options);
    };

    function applyHighlightCurrentRow(row) {
        document.querySelectorAll('.code-view .code-line.highlight-current').forEach(function (r) {
            r.classList.remove('highlight-current');
        });
        if (row) row.classList.add('highlight-current');
    }

    function scrollToHighlightLine(lineNum, smooth) {
        var wrap = document.querySelector('.preview-zoom-wrap') || document.querySelector('.code-view');
        if (wrap) {
            wrap.scrollTop = Math.max(0, (lineNum - 1) * PREVIEW_LINE_HEIGHT - wrap.clientHeight / 2);
        }
        var behavior = smooth ? 'smooth' : 'auto';
        requestAnimationFrame(function () {
            requestAnimationFrame(function () {
                var row = document.querySelector('.code-line[data-line-num="' + lineNum + '"]');
                if (row) {
                    row.scrollIntoView({ behavior: behavior, block: 'center' });
                    applyHighlightCurrentRow(row);
                }
            });
        });
    }

    window.initHighlightNav = function (lineNumbers) {
        _highlightLineNumbers = Array.isArray(lineNumbers) ? lineNumbers : [];
        _highlightLineIndex = -1;
        document.querySelectorAll('.code-view .code-line.highlight-current').forEach(function (r) {
            r.classList.remove('highlight-current');
        });
    };

    window.resetHighlightNav = function () {
        _highlightLineIndex = -1;
        _highlightLineNumbers = [];
        document.querySelectorAll('.code-view .code-line.highlight-current').forEach(function (r) {
            r.classList.remove('highlight-current');
        });
    };

    window.scrollToFirstHighlightInstant = function () {
        if (_highlightLineNumbers.length === 0) return null;
        _highlightLineIndex = 0;
        var lineNum = _highlightLineNumbers[0];
        scrollToHighlightLine(lineNum, false);
        return lineNum + '|' + 1 + '|' + _highlightLineNumbers.length;
    };

    window.scrollToNextHighlight = function (wrap) {
        if (wrap === undefined) wrap = true;
        if (_highlightLineNumbers.length === 0) return null;
        if (_highlightLineIndex < 0) {
            _highlightLineIndex = 0;
        } else {
            if (!wrap && _highlightLineIndex >= _highlightLineNumbers.length - 1) return null;
            _highlightLineIndex = wrap
                ? (_highlightLineIndex + 1) % _highlightLineNumbers.length
                : Math.min(_highlightLineIndex + 1, _highlightLineNumbers.length - 1);
        }
        var lineNum = _highlightLineNumbers[_highlightLineIndex];
        scrollToHighlightLine(lineNum, true);
        return lineNum + '|' + (_highlightLineIndex + 1) + '|' + _highlightLineNumbers.length;
    };

    window.scrollToPrevHighlight = function (wrap) {
        if (wrap === undefined) wrap = true;
        if (_highlightLineNumbers.length === 0) return null;
        if (_highlightLineIndex < 0) {
            _highlightLineIndex = _highlightLineNumbers.length - 1;
        } else {
            if (!wrap && _highlightLineIndex <= 0) return null;
            _highlightLineIndex = wrap
                ? (_highlightLineIndex <= 0 ? _highlightLineNumbers.length - 1 : _highlightLineIndex - 1)
                : Math.max(_highlightLineIndex - 1, 0);
        }
        var lineNum = _highlightLineNumbers[_highlightLineIndex];
        scrollToHighlightLine(lineNum, true);
        return lineNum + '|' + (_highlightLineIndex + 1) + '|' + _highlightLineNumbers.length;
    };
})();
