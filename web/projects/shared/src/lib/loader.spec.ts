import { Loader } from './loader';

describe('Loader', () => {
  it('goes from loading to ready', async () => {
    const loader = new Loader(() => Promise.resolve(42));
    expect(loader.loading()).toBe(true);
    await loader.load();
    expect(loader.loading()).toBe(false);
    expect(loader.value()).toBe(42);
    expect(loader.error()).toBeNull();
  });

  it('keeps the error so the page can offer a retry', async () => {
    const failure = new Error('down');
    let fail = true;
    const loader = new Loader(() => (fail ? Promise.reject(failure) : Promise.resolve('ok')));
    await loader.load();
    expect(loader.error()).toBe(failure);
    expect(loader.value()).toBeUndefined();
    fail = false;
    await loader.load();
    expect(loader.value()).toBe('ok');
  });

  it('a quiet reload keeps the current value on screen', async () => {
    let resolve!: (v: number) => void;
    const loader = new Loader(() => new Promise<number>((r) => (resolve = r)));
    loader.set(1);
    const reload = loader.load(true);
    expect(loader.value()).toBe(1);
    expect(loader.loading()).toBe(false);
    resolve(2);
    await reload;
    expect(loader.value()).toBe(2);
  });
});
