import { lazy, Suspense } from 'react';

const DebugInspector = lazy(() =>
  import('./DebugInspector').then((module) => ({ default: module.App })),
);

export function App() {
  return (
    <Suspense
      fallback={
        <main className="app-shell app-loading">
          <div className="empty-details">
            <span>Loading inspector</span>
          </div>
        </main>
      }
    >
      <DebugInspector />
    </Suspense>
  );
}
