import { act, render, screen } from '@testing-library/react';
import { BrowserRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import App from './App';
import { server } from './test/server';
import { antiforgery, contextIs } from './test/identityServer';

vi.mock('./AppRoutes', async () => {
  const React = await import('react');
  const ThrowingPage = () => { throw new Error('private render detail'); };

  return {
    default: [
      { path: '/boundary-broken', element: React.createElement(ThrowingPage) },
      { path: '/boundary-healthy', element: React.createElement('h1', null, 'Healthy route') },
    ],
  };
});

describe('application error boundary', () => {
  it('shows a neutral fallback for a render failure and resets it when the route changes', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});
    server.use(antiforgery(), contextIs(null));
    window.history.replaceState({}, '', '/boundary-broken');

    try {
      render(<BrowserRouter><App /></BrowserRouter>);

      const alert = await screen.findByRole('alert');
      expect(alert).toHaveTextContent('Something went wrong on this page. Reload to continue.');
      expect(screen.getByRole('button', { name: 'Reload' })).toBeInTheDocument();
      expect(screen.queryByText('private render detail')).not.toBeInTheDocument();

      act(() => {
        window.history.pushState({}, '', '/boundary-healthy');
        window.dispatchEvent(new PopStateEvent('popstate'));
      });

      expect(await screen.findByRole('heading', { name: 'Healthy route' })).toBeInTheDocument();
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    } finally {
      consoleError.mockRestore();
    }
  });
});
