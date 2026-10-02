export const notify = message => window.dispatchEvent(new CustomEvent('bench-toast',{detail:message}))
