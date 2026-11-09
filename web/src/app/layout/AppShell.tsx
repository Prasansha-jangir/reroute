import { Suspense } from 'react';
import { Link, NavLink, Outlet } from 'react-router';
import { CopilotDrawer } from '@/features/copilot';
import { useAuth } from '@/shared/auth/AuthProvider';
import { useUiStore } from '@/shared/store/uiStore';

export function AppShell() {
  const { user, logout } = useAuth();
  const openCopilot = useUiStore((s) => s.openCopilot);

  return (
    <>
      <a className="skip-link" href="#main">Skip to content</a>
      <header className="topbar">
        <Link to="/" className="brand">Reroute</Link>
        <nav aria-label="Main">
          <NavLink to="/flights">Flights</NavLink>
          <NavLink to="/claims">Claims</NavLink>
        </nav>
        <div className="row">
          <button className="secondary" onClick={() => openCopilot()}>Ask copilot</button>
          {user && <button className="secondary" onClick={logout}>Sign out {user.name}</button>}
        </div>
      </header>
      <main id="main">
        <Suspense fallback={<p aria-busy="true">Loading…</p>}>
          <Outlet />
        </Suspense>
      </main>
      <CopilotDrawer />
      <Toasts />
    </>
  );
}

function Toasts() {
  const { toasts, dismissToast } = useUiStore();
  return (
    <div className="toasts" aria-live="assertive">
      {toasts.map((t) => (
        <div key={t.id} className="toast" role="alert">
          <strong>{t.title}</strong>
          <p>{t.body}</p>
          <div className="row">
            {t.link && <Link to={t.link} onClick={() => dismissToast(t.id)}>View</Link>}
            <button className="secondary" onClick={() => dismissToast(t.id)} aria-label="Dismiss">Dismiss</button>
          </div>
        </div>
      ))}
    </div>
  );
}
