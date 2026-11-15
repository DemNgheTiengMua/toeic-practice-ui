import '@testing-library/jest-dom/vitest';
import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { SafeMarkdown } from '../SafeMarkdown';

describe('SafeMarkdown', () => {
  afterEach(() => {
    cleanup();
  });

  it('renders *italic* emphasis as <em> without literal asterisks in paragraphs and lists', () => {
    const content = [
      'Ví dụ: *"The man is wearing a hat."* là câu mô tả trực tiếp và **rất quan trọng**.',
      '',
      '- Chú ý *từ khóa* trong câu hỏi',
      '- Phân biệt *thì hiện tại tiếp diễn* với **thì hiện tại đơn**',
    ].join('\n');

    const { container } = render(<SafeMarkdown content={content} />);

    const ems = container.querySelectorAll('em');
    expect(ems.length).toBe(3);
    expect(ems[0]).toHaveTextContent('"The man is wearing a hat."');
    expect(ems[1]).toHaveTextContent('từ khóa');
    expect(ems[2]).toHaveTextContent('thì hiện tại tiếp diễn');

    const strongs = container.querySelectorAll('strong');
    expect(strongs.length).toBe(2);
    expect(strongs[0]).toHaveTextContent('rất quan trọng');
    expect(strongs[1]).toHaveTextContent('thì hiện tại đơn');

    expect(container.textContent).not.toContain('*');
  });

  it('keeps unmatched asterisks and raw HTML tags as literal text', () => {
    render(
      <SafeMarkdown content="Tính 2 * 3 + 4 * 5 và bỏ qua <script>alert(1)</script> với *nhấn mạnh*." />,
    );

    expect(screen.getByText(/2 \* 3 \+ 4 \* 5/)).toBeInTheDocument();
    expect(screen.getByText(/<script>alert\(1\)<\/script>/)).toBeInTheDocument();
    const em = screen.getByText('nhấn mạnh');
    expect(em.tagName).toBe('EM');
  });
});
