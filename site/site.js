// Reads the latest GitHub release to show its version, date and direct download links.
// Optional: the plain links in the HTML keep working without it, and a failed request changes nothing visible.
// Hooks (all optional, all start with the "hidden" attribute):
//   [data-release-version]     text "v1.2.3"
//   [data-release-date]        text "16 November 2026" (inside [data-release-date-wrap])
//   [data-release-installer]   href of the Setup exe (inside [data-release-links])
//   [data-release-portable]    href of SonicScrewdriver.exe
// Elements with the attributes data-release-*-wrap / data-release-links are un-hidden on success.
(function () {
  'use strict';
  var hooks = document.querySelectorAll('[data-release-version], [data-release-date], [data-release-installer], [data-release-portable]');
  if (!hooks.length || !window.fetch) return;

  function show(el) { while (el) { el.hidden = false; el = el.closest('[hidden]'); } }

  fetch('https://api.github.com/repos/leroidubid/SonicScrewdriver/releases/latest', { headers: { Accept: 'application/vnd.github+json' } })
    .then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
    .then(function (rel) {
      var assets = rel.assets || [];
      function find(re) {
        for (var i = 0; i < assets.length; i++) if (re.test(assets[i].name)) return assets[i].browser_download_url;
        return null;
      }
      var tag = rel.tag_name;
      var date = rel.published_at ? new Date(rel.published_at) : null;
      var installer = find(/^SonicScrewdriver-Setup-.*\.exe$/i);
      var portable = find(/^SonicScrewdriver\.exe$/i);
      document.querySelectorAll('[data-release-version]').forEach(function (el) {
        if (tag) { el.textContent = tag; show(el); }
      });
      document.querySelectorAll('[data-release-date]').forEach(function (el) {
        if (date && !isNaN(date)) {
          el.textContent = date.toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });
          show(el);
        }
      });
      document.querySelectorAll('[data-release-installer]').forEach(function (el) {
        if (installer) { el.href = installer; show(el); }
      });
      document.querySelectorAll('[data-release-portable]').forEach(function (el) {
        if (portable) { el.href = portable; show(el); }
      });
    })
    .catch(function () { /* keep the plain links */ });
})();
