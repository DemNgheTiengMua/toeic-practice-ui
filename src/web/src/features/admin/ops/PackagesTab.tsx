import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  Alert,
  AlertDialog,
  Button,
  Card,
  Chip,
  Input,
  Label,
  Modal,
  Spinner,
  Switch,
  Table,
  TextArea,
  TextField,
} from '@heroui/react';
import {
  AlertTriangle,
  BadgeCheck,
  CheckCircle2,
  Coins,
  DollarSign,
  Edit2,
  PackageCheck,
  Plus,
  RefreshCw,
  Sparkles,
} from 'lucide-react';
import { api, type PackageDto } from '../../../lib/api';
import { formatVnd, formatVndUnitPrice } from '../../../lib/security';

export function PackagesTab() {
  const queryClient = useQueryClient();

  // Create / Edit modal state
  const [isOpen, setIsOpen] = useState(false);
  const [editingPackageId, setEditingPackageId] = useState<number | null>(null);
  const [name, setName] = useState('');
  const [credits, setCredits] = useState('5');
  const [price, setPrice] = useState('199000');
  const [isActive, setIsActive] = useState(true);
  const [isFeatured, setIsFeatured] = useState(false);
  const [features, setFeatures] = useState('');
  const [displayOrder, setDisplayOrder] = useState('0');
  const [formError, setFormError] = useState<string | null>(null);

  // Deactivate AlertDialog state
  const [deactivatingPackage, setDeactivatingPackage] = useState<PackageDto | null>(null);

  const { data: summary, isLoading, error } = useQuery({
    queryKey: ['admin-packages'],
    queryFn: () => api.admin.packages(),
  });

  const saveMutation = useMutation({
    mutationFn: () => {
      const parsedCredits = parseInt(credits, 10);
      const parsedPrice = parseFloat(price);
      const parsedOrder = parseInt(displayOrder, 10) || 0;

      if (!name.trim()) {
        throw new Error('Package name is required.');
      }
      if (isNaN(parsedCredits) || parsedCredits <= 0) {
        throw new Error('Credits must be a positive integer.');
      }
      if (isNaN(parsedPrice) || parsedPrice < 0) {
        throw new Error('Price must be non-negative.');
      }
      if (isFeatured && !isActive) {
        throw new Error('An inactive package cannot be set as featured.');
      }

      const payload = {
        name: name.trim(),
        credits: parsedCredits,
        price: parsedPrice,
        isActive,
        isFeatured,
        features: features.trim() || null,
        displayOrder: parsedOrder,
      };

      if (editingPackageId !== null) {
        return api.admin.updatePackage(editingPackageId, payload);
      } else {
        return api.admin.createPackage(payload);
      }
    },
    onSuccess: () => {
      setIsOpen(false);
      resetForm();
      void queryClient.invalidateQueries({ queryKey: ['admin-packages'] });
    },
    onError: (err: any) => {
      setFormError(err?.message || 'Could not save package.');
    },
  });

  const deactivateMutation = useMutation({
    mutationFn: (pkgId: number) => api.admin.deactivatePackage(pkgId),
    onSuccess: () => {
      setDeactivatingPackage(null);
      void queryClient.invalidateQueries({ queryKey: ['admin-packages'] });
    },
  });

  function resetForm() {
    setEditingPackageId(null);
    setName('');
    setCredits('5');
    setPrice('199000');
    setIsActive(true);
    setIsFeatured(false);
    setFeatures('');
    setDisplayOrder('0');
    setFormError(null);
  }

  function openCreate() {
    resetForm();
    setIsOpen(true);
  }

  function openEdit(pkg: PackageDto) {
    setEditingPackageId(pkg.packageId);
    setName(pkg.name);
    setCredits(String(pkg.credits));
    setPrice(String(pkg.price));
    setIsActive(pkg.isActive);
    setIsFeatured(pkg.isFeatured);
    setFeatures(pkg.features || '');
    setDisplayOrder(String(pkg.displayOrder));
    setFormError(null);
    setIsOpen(true);
  }

  const numCredits = parseInt(credits, 10) || 0;
  const numPrice = parseFloat(price) || 0;
  const liveUnitPrice = numCredits > 0 ? formatVndUnitPrice(numPrice, numCredits) : '—';

  return (
    <div className="space-y-6">
      {/* Metric Tiles */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
        <Card className="border border-border">
          <Card.Content className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs uppercase tracking-wider text-muted font-medium">On sale</p>
              <p className="text-2xl font-semibold font-mono-tabular text-foreground mt-1">
                {summary ? summary.packagesOnSale : '—'}
              </p>
              <p className="text-xs text-muted mt-0.5">Active packages</p>
            </div>
            <div className="p-2.5 rounded-full bg-success/10 text-success">
              <PackageCheck className="h-5 w-5" />
            </div>
          </Card.Content>
        </Card>

        <Card className="border border-border">
          <Card.Content className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs uppercase tracking-wider text-muted font-medium">Featured</p>
              <p className="text-2xl font-semibold font-mono-tabular text-accent mt-1">
                {summary ? summary.featuredCount : '—'}
              </p>
              <p className="text-xs text-muted mt-0.5">Highlighted tier</p>
            </div>
            <div className="p-2.5 rounded-full bg-accent/10 text-accent">
              <Sparkles className="h-5 w-5" />
            </div>
          </Card.Content>
        </Card>

        <Card className="border border-border">
          <Card.Content className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs uppercase tracking-wider text-muted font-medium">Total sold</p>
              <p className="text-2xl font-semibold font-mono-tabular text-foreground mt-1">
                {summary ? summary.totalOrdersSold : '—'}
              </p>
              <p className="text-xs text-muted mt-0.5">Purchases across all tiers</p>
            </div>
            <div className="p-2.5 rounded-full bg-primary/10 text-primary">
              <Coins className="h-5 w-5" />
            </div>
          </Card.Content>
        </Card>

        <Card className="border border-border">
          <Card.Content className="p-4 flex items-center justify-between">
            <div>
              <p className="text-xs uppercase tracking-wider text-muted font-medium">Total revenue</p>
              <p className="text-2xl font-semibold font-mono-tabular text-foreground mt-1">
                {summary ? formatVnd(summary.totalRevenue) : '—'}
              </p>
              <p className="text-xs text-muted mt-0.5">Cumulative gross sales</p>
            </div>
            <div className="p-2.5 rounded-full bg-success/10 text-success">
              <DollarSign className="h-5 w-5" />
            </div>
          </Card.Content>
        </Card>
      </div>

      {/* Toolbar */}
      <div className="flex justify-between items-center">
        <div>
          <h3 className="text-base font-semibold text-foreground">Catalog packages</h3>
          <p className="text-xs text-muted">
            Configure credit bundles, pricing, and promotional highlights for learners.
          </p>
        </div>

        <Button size="sm" variant="primary" onPress={openCreate}>
          <Plus className="h-4 w-4 mr-1.5" />
          Create package
        </Button>
      </div>

      {/* Packages Table */}
      {isLoading ? (
        <div className="flex justify-center py-12">
          <Spinner size="lg" />
        </div>
      ) : error ? (
        <Alert status="danger">Could not load packages. Please try again.</Alert>
      ) : summary?.packages.length === 0 ? (
        <Card className="border border-border p-8 text-center text-muted">
          No credit packages configured yet.
        </Card>
      ) : (
        <Table>
          <Table.ScrollContainer>
            <Table.Content aria-label="Credit packages catalog table">
              <Table.Header>
                <Table.Column isRowHeader>Package name</Table.Column>
                <Table.Column>Credits</Table.Column>
                <Table.Column>Price</Table.Column>
                <Table.Column>Unit price</Table.Column>
                <Table.Column>Status</Table.Column>
                <Table.Column>Display order</Table.Column>
                <Table.Column>Performance</Table.Column>
                <Table.Column>Actions</Table.Column>
              </Table.Header>
              <Table.Body>
                {summary?.packages.map((pkg) => (
                  <Table.Row key={pkg.packageId}>
                    <Table.Cell>
                      <div className="flex items-center gap-2">
                        <span className="font-semibold text-foreground">{pkg.name}</span>
                        {pkg.isFeatured && (
                          <Chip size="sm" color="accent" variant="soft">
                            <Sparkles className="h-3 w-3 mr-1" />
                            Featured
                          </Chip>
                        )}
                      </div>
                    </Table.Cell>
                    <Table.Cell>
                      <span className="font-mono-tabular font-medium text-foreground">
                        {pkg.credits} credits
                      </span>
                    </Table.Cell>
                    <Table.Cell>
                      <span className="font-mono-tabular font-medium text-foreground">
                        {formatVnd(pkg.price)}
                      </span>
                    </Table.Cell>
                    <Table.Cell>
                      <span className="font-mono-tabular text-xs text-muted">
                        {formatVndUnitPrice(pkg.price, pkg.credits)}
                      </span>
                    </Table.Cell>
                    <Table.Cell>
                      <Chip
                        size="sm"
                        color={pkg.isActive ? 'success' : 'default'}
                        variant="soft"
                      >
                        {pkg.isActive ? 'Active' : 'Inactive'}
                      </Chip>
                    </Table.Cell>
                    <Table.Cell>
                      <span className="font-mono-tabular text-muted text-xs">
                        {pkg.displayOrder}
                      </span>
                    </Table.Cell>
                    <Table.Cell>
                      <div>
                        <p className="text-xs font-mono-tabular text-foreground">
                          {pkg.ordersSold ?? 0} sold
                        </p>
                        <p className="text-xs font-mono-tabular text-muted">
                          {formatVnd(pkg.totalRevenue ?? 0)}
                        </p>
                      </div>
                    </Table.Cell>
                    <Table.Cell>
                      <div className="flex items-center gap-2">
                        <Button size="sm" variant="secondary" onPress={() => openEdit(pkg)}>
                          <Edit2 className="h-3.5 w-3.5 mr-1" />
                          Edit
                        </Button>
                        {pkg.isActive && (
                          <Button
                            size="sm"
                            variant="danger"
                            onPress={() => setDeactivatingPackage(pkg)}
                          >
                            Deactivate
                          </Button>
                        )}
                      </div>
                    </Table.Cell>
                  </Table.Row>
                ))}
              </Table.Body>
            </Table.Content>
          </Table.ScrollContainer>
        </Table>
      )}

      {/* Create / Edit Modal */}
      {isOpen && (
        <Modal.Backdrop
          isOpen={isOpen}
          onOpenChange={(open) => {
            if (!open) setIsOpen(false);
          }}
        >
          <Modal.Container placement="center">
            <Modal.Dialog className="sm:max-w-lg">
              <Modal.CloseTrigger />
              <Modal.Header>
                <Modal.Heading>
                  {editingPackageId !== null ? `Edit package — ${name}` : 'Create credit package'}
                </Modal.Heading>
                <p className="text-xs text-muted mt-1">
                  Define package properties, pricing, and promotional marketing bullet points.
                </p>
              </Modal.Header>

              <Modal.Body className="space-y-4">
                {formError && <Alert status="danger">{formError}</Alert>}

                <TextField className="w-full" name="name" isRequired>
                  <Label>Package name</Label>
                  <Input
                    placeholder="e.g. Starter Pack"
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                  />
                </TextField>

                <div className="grid grid-cols-2 gap-3">
                  <TextField className="w-full" name="credits" isRequired>
                    <Label>Credits</Label>
                    <Input
                      type="number"
                      placeholder="e.g. 5"
                      value={credits}
                      onChange={(e) => setCredits(e.target.value)}
                    />
                  </TextField>

                  <TextField className="w-full" name="price" isRequired>
                    <Label>Price (VND)</Label>
                    <Input
                      type="number"
                      placeholder="e.g. 199000"
                      value={price}
                      onChange={(e) => setPrice(e.target.value)}
                    />
                  </TextField>
                </div>

                {/* Unit price calculation hint */}
                <div className="p-2.5 rounded bg-surface-secondary border border-border flex items-center justify-between text-xs">
                  <span className="text-muted">Calculated unit price:</span>
                  <span className="font-semibold font-mono-tabular text-foreground">
                    {liveUnitPrice}
                  </span>
                </div>

                <div className="grid grid-cols-2 gap-4 pt-1">
                  <div className="flex items-center justify-between p-3 rounded-md border border-border">
                    <div>
                      <p className="text-sm font-medium text-foreground">Active for sale</p>
                      <p className="text-xs text-muted">Learners can purchase</p>
                    </div>
                    <Switch
                      isSelected={isActive}
                      onChange={(checked) => {
                        setIsActive(checked);
                        if (!checked) setIsFeatured(false);
                      }}
                    />
                  </div>

                  <div className="flex items-center justify-between p-3 rounded-md border border-border">
                    <div>
                      <p className="text-sm font-medium text-foreground">Featured</p>
                      <p className="text-xs text-muted">Highlight on pricing</p>
                    </div>
                    <Switch
                      isSelected={isFeatured}
                      isDisabled={!isActive}
                      onChange={(checked) => setIsFeatured(checked)}
                    />
                  </div>
                </div>

                <TextField className="w-full" name="displayOrder">
                  <Label>Display order (lowest first)</Label>
                  <Input
                    type="number"
                    placeholder="0"
                    value={displayOrder}
                    onChange={(e) => setDisplayOrder(e.target.value)}
                  />
                </TextField>

                <TextField className="w-full" name="features">
                  <Label>Marketing features (one per line)</Label>
                  <TextArea
                    rows={4}
                    placeholder="5 full mock exam attempts&#10;Unlimited practice sessions&#10;Priority grading queue"
                    value={features}
                    onChange={(e) => setFeatures(e.target.value)}
                  />
                </TextField>
              </Modal.Body>

              <Modal.Footer>
                <Button variant="secondary" onPress={() => setIsOpen(false)}>
                  Cancel
                </Button>
                <Button
                  variant="primary"
                  isDisabled={saveMutation.isPending}
                  onPress={() => saveMutation.mutate()}
                >
                  {saveMutation.isPending ? 'Saving...' : 'Save package'}
                </Button>
              </Modal.Footer>
            </Modal.Dialog>
          </Modal.Container>
        </Modal.Backdrop>
      )}

      {/* Deactivate AlertDialog */}
      {deactivatingPackage && (
        <AlertDialog.Backdrop
          isOpen={deactivatingPackage !== null}
          onOpenChange={(open) => {
            if (!open) setDeactivatingPackage(null);
          }}
        >
          <AlertDialog.Container placement="center">
            <AlertDialog.Dialog className="sm:max-w-md">
              <AlertDialog.Header>
                <div className="flex items-center gap-2">
                  <AlertTriangle className="h-5 w-5 text-danger" />
                  <AlertDialog.Heading>Deactivate package?</AlertDialog.Heading>
                </div>
              </AlertDialog.Header>

              <AlertDialog.Body>
                <p className="text-sm text-muted">
                  Are you sure you want to deactivate{' '}
                  <span className="font-semibold text-foreground">
                    {deactivatingPackage.name}
                  </span>
                  ? It will no longer be visible in the catalog or checkout, but existing orders and
                  transaction history remain intact.
                </p>
              </AlertDialog.Body>

              <AlertDialog.Footer>
                <Button variant="secondary" onPress={() => setDeactivatingPackage(null)}>
                  Cancel
                </Button>
                <Button
                  variant="danger"
                  isDisabled={deactivateMutation.isPending}
                  onPress={() => deactivateMutation.mutate(deactivatingPackage.packageId)}
                >
                  {deactivateMutation.isPending ? 'Deactivating...' : 'Confirm deactivation'}
                </Button>
              </AlertDialog.Footer>
            </AlertDialog.Dialog>
          </AlertDialog.Container>
        </AlertDialog.Backdrop>
      )}
    </div>
  );
}
