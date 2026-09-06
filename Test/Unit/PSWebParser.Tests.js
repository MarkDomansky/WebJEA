'use strict';

const { escapeHtml, sanitizeUrl, isSafeCssClass, processTagMarkup } = require('../../WebJEA/wwwroot/resources/PSWebParser.js');

// ── escapeHtml ────────────────────────────────────────────────────────────────

describe('escapeHtml', () => {
    test('escapes angle brackets', () => {
        expect(escapeHtml('<script>alert(1)</script>')).toBe('&lt;script&gt;alert(1)&lt;/script&gt;');
    });

    test('escapes ampersands', () => {
        expect(escapeHtml('a & b')).toBe('a &amp; b');
    });

    test('escapes double quotes', () => {
        expect(escapeHtml('"quoted"')).toBe('&quot;quoted&quot;');
    });

    test('escapes single quotes', () => {
        expect(escapeHtml("it's")).toBe('it&#39;s');
    });

    test('returns empty string for null', () => {
        expect(escapeHtml(null)).toBe('');
    });

    test('returns empty string for undefined', () => {
        expect(escapeHtml(undefined)).toBe('');
    });

    test('leaves safe text unchanged', () => {
        expect(escapeHtml('Hello, world!')).toBe('Hello, world!');
    });
});

// ── sanitizeUrl ───────────────────────────────────────────────────────────────

describe('sanitizeUrl', () => {
    test('allows https URLs', () => {
        expect(sanitizeUrl('https://example.com/path')).toBe('https://example.com/path');
    });

    test('allows http URLs', () => {
        expect(sanitizeUrl('http://example.com')).toBe('http://example.com');
    });

    test('allows relative paths', () => {
        expect(sanitizeUrl('/resources/img.png')).toBe('/resources/img.png');
    });

    test('blocks javascript: scheme', () => {
        expect(sanitizeUrl('javascript:alert(1)')).toBe('#');
    });

    test('blocks javascript: with mixed case', () => {
        expect(sanitizeUrl('JavaScript:alert(1)')).toBe('#');
    });

    test('blocks javascript: with leading whitespace', () => {
        expect(sanitizeUrl('  javascript:alert(1)')).toBe('#');
    });

    test('blocks javascript: with leading null bytes', () => {
        expect(sanitizeUrl('\x00javascript:alert(1)')).toBe('#');
    });

    test('blocks data: scheme', () => {
        expect(sanitizeUrl('data:text/html,<script>alert(1)</script>')).toBe('#');
    });

    test('blocks data: with mixed case', () => {
        expect(sanitizeUrl('DATA:text/html,bad')).toBe('#');
    });

    test('returns # for null', () => {
        expect(sanitizeUrl(null)).toBe('#');
    });

    test('returns # for empty string', () => {
        expect(sanitizeUrl('')).toBe('#');
    });
});

// ── isSafeCssClass ────────────────────────────────────────────────────────────

describe('isSafeCssClass', () => {
    test('allows alphanumeric class names', () => {
        expect(isSafeCssClass('psoutput')).toBe(true);
    });

    test('allows hyphens', () => {
        expect(isSafeCssClass('ps-output-table')).toBe(true);
    });

    test('allows underscores', () => {
        expect(isSafeCssClass('ps_output')).toBe(true);
    });

    test('rejects spaces', () => {
        expect(isSafeCssClass('two classes')).toBe(false);
    });

    test('rejects angle brackets', () => {
        expect(isSafeCssClass('<script>')).toBe(false);
    });

    test('rejects quotes', () => {
        expect(isSafeCssClass('x" onmouseover="bad')).toBe(false);
    });

    test('rejects empty string', () => {
        expect(isSafeCssClass('')).toBe(false);
    });
});

// ── processTagMarkup ──────────────────────────────────────────────────────────

describe('processTagMarkup - anchor tags', () => {
    test('converts [[a|url|label]] to an anchor with rel', () => {
        const result = processTagMarkup('[[a|https://example.com|Click here]]');
        expect(result).toBe('<a href="https://example.com" rel="noopener noreferrer">Click here</a>');
    });

    test('blocks javascript: in href', () => {
        const result = processTagMarkup('[[a|javascript:alert(1)|xss]]');
        expect(result).toContain('href="#"');
        expect(result).not.toContain('javascript:');
    });

    test('blocks data: in href', () => {
        const result = processTagMarkup('[[a|data:text/html,bad|xss]]');
        expect(result).toContain('href="#"');
    });

    test('includes rel="noopener noreferrer"', () => {
        const result = processTagMarkup('[[a|https://example.com|link]]');
        expect(result).toContain('rel="noopener noreferrer"');
    });

    test('is case-insensitive on tag name', () => {
        const result = processTagMarkup('[[A|https://example.com|Link]]');
        expect(result).toContain('<a href=');
    });

    test('handles multiple anchor tags', () => {
        const result = processTagMarkup('[[a|https://one.com|One]] and [[a|https://two.com|Two]]');
        expect(result).toContain('href="https://one.com"');
        expect(result).toContain('href="https://two.com"');
    });

    test('leaves text outside tags unchanged', () => {
        const result = processTagMarkup('before [[a|https://example.com|link]] after');
        expect(result).toMatch(/^before .* after$/);
    });
});

describe('processTagMarkup - span tags', () => {
    test('converts [[span|cssclass|text]] to a span', () => {
        const result = processTagMarkup('[[span|pserror|bad output]]');
        expect(result).toBe('<span class="pserror">bad output</span>');
    });

    test('strips span when class name is unsafe', () => {
        const result = processTagMarkup('[[span|a b|text]]');
        expect(result).toBe('text');
        expect(result).not.toContain('<span');
    });

    test('strips span with injection attempt in class', () => {
        const result = processTagMarkup('[[span|x" onmouseover="bad|text]]');
        expect(result).not.toContain('onmouseover');
        expect(result).not.toContain('<span');
    });
});

describe('processTagMarkup - img tags', () => {
    test('converts [[img|cssclass|src]] to an img', () => {
        const result = processTagMarkup('[[img|ps-icon|/resources/ps.png]]');
        expect(result).toBe('<img class="ps-icon" src="/resources/ps.png" />');
    });

    test('blocks javascript: in img src', () => {
        const result = processTagMarkup('[[img|cls|javascript:alert(1)]]');
        expect(result).toContain('src="#"');
        expect(result).not.toContain('javascript:');
    });

    test('omits class attribute when class name is unsafe', () => {
        const result = processTagMarkup('[[img|bad class|/img.png]]');
        expect(result).not.toContain('class=');
    });

    test('allows empty class for img', () => {
        const result = processTagMarkup('[[img||/img.png]]');
        expect(result).toBe('<img src="/img.png" />');
    });
});

describe('processTagMarkup - no matching tags', () => {
    test('returns plain text unchanged', () => {
        expect(processTagMarkup('just plain text')).toBe('just plain text');
    });

    test('ignores unrecognized tag syntax', () => {
        const input = '[[unknown|foo|bar]]';
        expect(processTagMarkup(input)).toBe(input);
    });
});
