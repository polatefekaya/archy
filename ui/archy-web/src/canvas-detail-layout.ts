import type { ModuleDetailScene, RemoteFileScene } from './canvas-detail-scene';
import type { ModuleBounds } from './canvas-layout';

export type ModuleDetailLayout = {
  focusBounds: ModuleBounds;
  fileBoundsById: ReadonlyMap<string, ModuleBounds>;
  remoteFileBoundsById: ReadonlyMap<string, ModuleBounds>;
  width: number;
  height: number;
};

export const FILE_CARD_WIDTH = 220;
export const FILE_CARD_HEIGHT = 82;
export const REMOTE_FILE_CARD_WIDTH = 220;
export const REMOTE_FILE_CARD_HEIGHT = 72;

const GAP = 16;
const PADDING = 32;
const FOCUS_HEADER = 82;
const RAIL_GAP = 42;
const MAX_FILE_COLUMNS = 3;
const MAX_REMOTE_FILE_COLUMNS = 4;

/**
 * A directory detail scene is a layered file-to-file map. Incoming neighbour files
 * are above the focused module and outgoing neighbour files are below it, keeping
 * the actual dependency lines short, unambiguous, and readable at fit width.
 */
export function layoutModuleDetail(detail: ModuleDetailScene, maxFileColumns = MAX_FILE_COLUMNS): ModuleDetailLayout {
  const fileColumns = Math.max(1, Math.min(maxFileColumns, Math.max(1, detail.files.length)));
  const fileRows = Math.max(1, Math.ceil(detail.files.length / fileColumns));
  const focusWidth = PADDING * 2 + fileColumns * FILE_CARD_WIDTH + Math.max(0, fileColumns - 1) * GAP;
  const focusHeight = FOCUS_HEADER + PADDING + fileRows * FILE_CARD_HEIGHT + Math.max(0, fileRows - 1) * GAP + PADDING;

  const directionFor = (remote: RemoteFileScene) => remote.incomingConnectionCount > 0 ? 'incoming' : 'outgoing';
  const incoming = detail.remoteFiles.filter(file => directionFor(file) === 'incoming');
  const outgoing = detail.remoteFiles.filter(file => directionFor(file) === 'outgoing');
  const columnCount = (count: number) => Math.max(1, Math.min(MAX_REMOTE_FILE_COLUMNS, count));
  const railWidth = (count: number) => {
    const columns = columnCount(count);
    return count ? columns * REMOTE_FILE_CARD_WIDTH + Math.max(0, columns - 1) * GAP : 0;
  };
  const railHeight = (count: number) => {
    const columns = columnCount(count);
    const rows = count ? Math.ceil(count / columns) : 0;
    return rows ? rows * REMOTE_FILE_CARD_HEIGHT + Math.max(0, rows - 1) * GAP : 0;
  };
  const incomingHeight = railHeight(incoming.length);
  const outgoingHeight = railHeight(outgoing.length);
  const width = Math.max(focusWidth, railWidth(incoming.length), railWidth(outgoing.length)) + PADDING * 2;
  const focusX = (width - focusWidth) / 2;
  const focusY = PADDING + incomingHeight + (incomingHeight ? RAIL_GAP : 0);
  const focusBounds = { x: focusX, y: focusY, width: focusWidth, height: focusHeight };

  const fileBoundsById = new Map<string, ModuleBounds>();
  detail.files.forEach((file, index) => fileBoundsById.set(file.id, {
    x: focusX + PADDING + (index % fileColumns) * (FILE_CARD_WIDTH + GAP),
    y: focusY + FOCUS_HEADER + Math.floor(index / fileColumns) * (FILE_CARD_HEIGHT + GAP),
    width: FILE_CARD_WIDTH,
    height: FILE_CARD_HEIGHT,
  }));

  const remoteFileBoundsById = new Map<string, ModuleBounds>();
  const placeRail = (files: readonly RemoteFileScene[], y: number) => {
    const columns = columnCount(files.length);
    const rowWidth = railWidth(files.length);
    const startX = (width - rowWidth) / 2;
    files.forEach((file, index) => remoteFileBoundsById.set(file.id, {
      x: startX + (index % columns) * (REMOTE_FILE_CARD_WIDTH + GAP),
      y: y + Math.floor(index / columns) * (REMOTE_FILE_CARD_HEIGHT + GAP),
      width: REMOTE_FILE_CARD_WIDTH,
      height: REMOTE_FILE_CARD_HEIGHT,
    }));
  };
  placeRail(incoming, PADDING);
  placeRail(outgoing, focusY + focusHeight + (outgoingHeight ? RAIL_GAP : 0));

  return {
    focusBounds,
    fileBoundsById,
    remoteFileBoundsById,
    width,
    height: focusY + focusHeight + (outgoingHeight ? RAIL_GAP + outgoingHeight : 0) + PADDING,
  };
}
