import { Link } from '@tanstack/react-router';
import { Button, Card, Chip } from '@heroui/react';
import { Compass, Sparkles, ArrowRight, Users } from 'lucide-react';

export function OobeSoftLockEmptyState() {
  return (
    <div className="mx-auto max-w-2xl py-10" data-testid="oobe-softlock-empty-state">
      <Card variant="default" className="border border-border/80 bg-surface shadow-sm">
        <Card.Header className="flex flex-col items-start gap-2 pb-2">
          <div className="flex items-center gap-2">
            <Chip size="sm" variant="soft" color="accent">
              <Sparkles className="h-3.5 w-3.5" />
              Guided Setup Required
            </Chip>
          </div>
          <Card.Title className="text-xl font-semibold">
            Hoàn tất thiết lập lộ trình học (OOBE) để mở khóa khu vực này
          </Card.Title>
          <Card.Description>
            Bạn cần hoàn thành bài kiểm tra đầu vào (Universal Placement), chọn mục tiêu chứng chỉ và tạo lộ trình học tập đầu tiên trước khi truy cập bảng điều khiển và bài luyện tập.
          </Card.Description>
        </Card.Header>
        <Card.Content className="flex flex-wrap items-center gap-3 pt-2">
          <Link to="/onboarding" className="inline-flex">
            <Button variant="primary">
              <Compass className="h-4 w-4" />
              Continue OOBE setup
              <ArrowRight className="h-4 w-4" />
            </Button>
          </Link>
          <Link to="/mentors" className="inline-flex">
            <Button variant="secondary">
              <Users className="h-4 w-4" />
              Explore Mentors
            </Button>
          </Link>
        </Card.Content>
      </Card>
    </div>
  );
}
