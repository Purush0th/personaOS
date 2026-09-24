import { Provider } from '@angular/core';
import { CDK_DRAG_CONFIG, DragDropConfig } from '@angular/cdk/drag-drop';

/**
 * Cards and rows drag from anywhere on them, not from a grip. With a mouse the drag starts as soon
 * as the pointer moves; a click without movement still follows the card's link. On a touch screen
 * a card needs a short press before it lifts, so a swipe across the board still scrolls it.
 *
 * Provided by the two pages that drag (board and backlog), so drag-drop stays out of the others.
 */
export const DRAG_DEFAULTS: Provider = {
  provide: CDK_DRAG_CONFIG,
  useValue: { dragStartDelay: { touch: 250, mouse: 0 } } satisfies DragDropConfig,
};
