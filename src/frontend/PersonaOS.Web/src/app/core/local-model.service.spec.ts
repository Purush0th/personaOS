import { readPullLines } from './local-model.service';

describe('readPullLines', () => {
  it('reads every complete line', () => {
    expect(readPullLines('{"status":"pulling manifest"}\n{"status":"pulling 1a","completed":5,"total":10}\n')).toEqual([
      { status: 'pulling manifest' },
      { status: 'pulling 1a', completed: 5, total: 10 },
    ]);
  });

  it('leaves a line still arriving for later', () => {
    expect(readPullLines('{"status":"success"}\n{"do')).toEqual([{ status: 'success' }]);
  });

  it('reads the end of the download and an error', () => {
    expect(readPullLines('{"done":true}\n')).toEqual([{ done: true }]);
    expect(readPullLines('{"error":"no space left"}\n')).toEqual([{ error: 'no space left' }]);
  });
});
