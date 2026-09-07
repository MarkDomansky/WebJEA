# Tips

General tips on how to do things that WebJEA doesn't currently support.

## Adding a password field

Sometimes you want a form field to be masked. WebJEA renders parameters as plain
text inputs, but each input's `id` is the parameter name, so a small addition to
`resources/startup.js` (in the deployed site folder) can switch chosen fields to
password inputs.

Replace `Password` with the name of the parameter you want masked. If you use a
generic name such as `$Password` across your scripts, one entry covers all of
them; add more names to the array to mask several fields.

```js
// startup.js — mask the listed parameter names
$(function () {
    var maskedFields = ['Password']; // add each parameter name to mask

    maskedFields.forEach(function (name) {
        // The rendered id is the parameter name. The [id$=] fallback also
        // matches if a future layout nests the inputs in a naming container.
        $('#' + name + ', input[id$="_' + name + '"]')
            .filter('input[type="text"]')
            .attr('type', 'password');
    });
});
```

The form is rendered server-side, so the controls already exist when the document
is ready — no need to watch for them.

**This is presentation only.** Masking the input stops someone reading the value
over the user's shoulder; it does not change how the value is transmitted or
handled. Set `LogParameters` to `false` on any command taking a secret (see
[Usage](Usage.md)), or the value is written to the audit log in clear text, and
make sure the site is served over HTTPS.
