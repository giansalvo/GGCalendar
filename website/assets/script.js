// Offuscamento email: l'indirizzo non compare mai in chiaro nell'HTML.
(function () {
  var parts = ['profgusinu', 'gmail', 'com'];
  function build() {
    var addr = parts[0] + String.fromCharCode(64) + parts[1] + String.fromCharCode(46) + parts[2];
    document.querySelectorAll('[data-mail]').forEach(function (el) {
      var subject = el.getAttribute('data-subject');
      var href = 'mailto:' + addr + (subject ? '?subject=' + encodeURIComponent(subject) : '');
      el.setAttribute('href', href);
      if (el.hasAttribute('data-show')) el.textContent = addr;
    });
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', build);
  else build();
})();

// Language: the home page opens in the language of the browser the first time (Italian is the default and the reference text);
// the language links remember the choice. Only the home page is redirected: privacy and terms stay where the link points.
(function () {
  try {
    var links = document.querySelectorAll('.langs a');
    links.forEach(function (a) {
      a.addEventListener('click', function () { localStorage.setItem('ggc-lang', a.getAttribute('hreflang')); });
    });
    var inFolder = /\/(en|de|es|fr)\//.test(location.pathname);
    var isHome = /(\/|index\.html)$/.test(location.pathname);
    if (inFolder || !isHome || localStorage.getItem('ggc-lang')) return;
    var wanted = (navigator.language || 'it').slice(0, 2).toLowerCase();
    if (wanted === 'it') return;
    var target = ['en', 'de', 'es', 'fr'].indexOf(wanted) >= 0 ? wanted : 'en';
    location.replace(target + '/index.html');
  } catch (e) { /* no storage or no navigator: the page stays as it is */ }
})();
