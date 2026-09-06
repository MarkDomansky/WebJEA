# Tips

General tips on how to do things that WebJEA doesn't currently support.

## Adding a password field

Sometimes you want a form field to be masked. WebJEA renders `SecureString`/
`PSCredential` parameters as plain text inputs, but each input's `id` is the
parameter name, so a small addition to `resources/startup.js` (in the site folder)
can switch chosen fields to password inputs.

The form is rendered dynamically after the page loads, so the change has to be
applied when the field appears rather than at document-ready:

```js
// startup.js — mask the listed parameter names wherever they appear
(function () {
    var maskedFields = ['Password']; // add each parameter name to mask

    function mask() {
        maskedFields.forEach(function (name) {
            var el = document.getElementById(name);
            if (el && el.tagName === 'INPUT' && el.type === 'text') {
                el.type = 'password';
            }
        });
    }

    var container = document.getElementById('divParameters');
    if (container) {
        new MutationObserver(mask).observe(container, { childList: true });
        mask();
    }
})();
```

If you use a generic name like `$Password` in all of your scripts, one entry in
`maskedFields` covers every form.

A few cautions:

* Masking is cosmetic — the value still travels to the server as an ordinary string
  parameter. **Use HTTPS.**
* Set `"LogParameters": false` on any command that takes a secret, or the value will
  be written to the audit log ([Usage](Usage.md)).
* `startup.js` ships with the site files, so re-apply your customization after an
  upgrade.
