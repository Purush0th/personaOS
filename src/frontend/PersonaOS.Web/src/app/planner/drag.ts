/** How far the pointer moves before a press on an event is a drag rather than a click. */
export const DRAG_THRESHOLD_PX = 4;

/**
 * Stops the click the browser sends when a drag ends. It lands on whatever the drag started and
 * ended over in common (the event, or the day under it), which would open the event or start a
 * new one there. A drag that ends with no click (released outside the window) leaves nothing
 * waiting.
 */
export function swallowNextClick(): void {
  const swallow = (event: MouseEvent) => {
    event.stopPropagation();
    event.preventDefault();
  };
  document.addEventListener('click', swallow, { capture: true, once: true });
  setTimeout(() => document.removeEventListener('click', swallow, { capture: true }));
}
