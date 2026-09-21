(() => {
 const tabs = [...document.querySelectorAll('[data-channel]')];
 function activate(channel, focus = false) {
  const current = tabs.find(tab => tab.dataset.channel === channel) || tabs.find(tab => tab.dataset.channel === 'preview');
  for (const tab of tabs) {
   const selected = tab === current;
   tab.setAttribute('aria-selected', String(selected));
   tab.tabIndex = selected ? 0 : -1;
   document.getElementById(tab.getAttribute('aria-controls')).hidden = !selected;
  }
  if (focus) current.focus();
 }
 for (const [index, tab] of tabs.entries()) {
  tab.addEventListener('click', () => {
   activate(tab.dataset.channel);
   history.replaceState(null, '', '#' + tab.dataset.channel);
  });
  tab.addEventListener('keydown', event => {
   let next;
   if (event.key === 'ArrowRight') next = (index + 1) % tabs.length;
   else if (event.key === 'ArrowLeft') next = (index + tabs.length - 1) % tabs.length;
   else if (event.key === 'Home') next = 0;
   else if (event.key === 'End') next = tabs.length - 1;
   else return;
   event.preventDefault();
   activate(tabs[next].dataset.channel, true);
   history.replaceState(null, '', '#' + tabs[next].dataset.channel);
  });
 }
 window.addEventListener('hashchange', () => activate(location.hash.slice(1)));
 activate(location.hash.slice(1));
})();
