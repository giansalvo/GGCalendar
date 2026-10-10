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
