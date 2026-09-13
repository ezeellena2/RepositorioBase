import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import TablePagination from '@mui/material/TablePagination';
import App from './App';
import { server } from './test/server';
import { antiforgery, contextIs } from './test/identityServer';

vi.mock('./AppRoutes', () => {
  const paginationComponent = 'div';
  return { default: [{ path: '/', element: <TablePagination component={paginationComponent} count={0} page={0} rowsPerPage={10} onPageChange={() => {}} /> }] };
});

async function chooseShellLanguage(label, option) {
  await userEvent.click(screen.getByRole('combobox', { name: label }));
  await userEvent.click(screen.getByRole('option', { name: option }));
}

describe('localized application theme', () => {
  it('updates MUI component copy with the shell language and switches back to English', async () => {
    server.use(antiforgery(), contextIs(null));
    render(<MemoryRouter><App /></MemoryRouter>);
    await screen.findByRole('link', { name: 'Log in' });
    expect(screen.getByText('Rows per page:')).toBeVisible();
    await chooseShellLanguage('Language', 'Español');
    expect(screen.getByText('Filas por página:')).toBeVisible();
    await chooseShellLanguage('Idioma', 'English');
    expect(screen.getByText('Rows per page:')).toBeVisible();
  });
});
