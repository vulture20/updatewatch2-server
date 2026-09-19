import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { buildPageNumbers, Pagination } from './Pagination';

describe('buildPageNumbers', () => {
  it('returns an empty list when there are no pages', () => {
    expect(buildPageNumbers(1, 0)).toEqual([]);
  });

  it('returns just page 1 when there is only one page', () => {
    expect(buildPageNumbers(1, 1)).toEqual([1]);
  });

  it('shows every page with no ellipsis when the total is small', () => {
    expect(buildPageNumbers(1, 4)).toEqual([1, 2, 3, 4]);
    expect(buildPageNumbers(3, 5)).toEqual([1, 2, 3, 4, 5]);
  });

  it('shows only a trailing ellipsis when current is near the start', () => {
    expect(buildPageNumbers(1, 20)).toEqual([1, 2, 'ellipsis-after', 20]);
  });

  it('shows only a leading ellipsis when current is near the end', () => {
    expect(buildPageNumbers(20, 20)).toEqual([1, 'ellipsis-before', 19, 20]);
  });

  it('shows both ellipses when current is in the middle', () => {
    expect(buildPageNumbers(10, 20)).toEqual([1, 'ellipsis-before', 9, 10, 11, 'ellipsis-after', 20]);
  });
});

describe('Pagination', () => {
  it('disables Previous on the first page and Next on the last page', () => {
    render(<Pagination page={1} totalPages={3} onPageChange={vi.fn()} />);

    expect(screen.getByRole('button', { name: 'Previous' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Next' })).toBeEnabled();
  });

  it('enables both buttons on a middle page', () => {
    render(<Pagination page={2} totalPages={3} onPageChange={vi.fn()} />);

    expect(screen.getByRole('button', { name: 'Previous' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Next' })).toBeEnabled();
  });

  it('disables Next on the last page', () => {
    render(<Pagination page={3} totalPages={3} onPageChange={vi.fn()} />);

    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled();
  });

  it('shows the page indicator text', () => {
    render(<Pagination page={2} totalPages={5} onPageChange={vi.fn()} />);

    expect(screen.getByText('Page 2 of 5')).toBeInTheDocument();
  });

  it('marks the active page button as current and disabled', () => {
    render(<Pagination page={2} totalPages={5} onPageChange={vi.fn()} />);

    const activeButton = screen.getByRole('button', { name: 'Page 2' });
    expect(activeButton).toHaveAttribute('aria-current', 'page');
    expect(activeButton).toBeDisabled();
  });

  it('calls onPageChange with the clicked page number', async () => {
    const onPageChange = vi.fn();
    const user = userEvent.setup();
    render(<Pagination page={1} totalPages={5} onPageChange={onPageChange} />);

    // With current=1/total=5, buildPageNumbers renders [1, 2, ellipsis, 5] —
    // page 5 (the last page) is always present regardless of current page.
    await user.click(screen.getByRole('button', { name: 'Page 5' }));

    expect(onPageChange).toHaveBeenCalledWith(5);
  });

  it('calls onPageChange with page - 1 / page + 1 for Previous/Next', async () => {
    const onPageChange = vi.fn();
    const user = userEvent.setup();
    render(<Pagination page={2} totalPages={5} onPageChange={onPageChange} />);

    await user.click(screen.getByRole('button', { name: 'Previous' }));
    expect(onPageChange).toHaveBeenCalledWith(1);

    await user.click(screen.getByRole('button', { name: 'Next' }));
    expect(onPageChange).toHaveBeenCalledWith(3);
  });
});
