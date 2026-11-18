export function validateCartAgainstInventory(cart, products) {
  if (!Array.isArray(cart) || cart.length === 0 || !Array.isArray(products)) return false;
  const seenProductIds = new Set();

  return cart.every(line => {
    if (!line || line.id === undefined || line.id === null
      || !Number.isInteger(line.qty) || line.qty < 1
      || seenProductIds.has(line.id)) return false;
    seenProductIds.add(line.id);

    const matches = products.filter(item => item?.id === line.id);
    if (matches.length !== 1) return false;
    const product = matches[0];
    return Number.isFinite(product.stock) && product.stock >= line.qty
      && Number.isFinite(product.price) && product.price >= 0
      && Number.isSafeInteger(product.sold) && product.sold >= 0;
  });
}

export function calculateCartTotal(cart, products) {
  if (!validateCartAgainstInventory(cart, products)) return null;
  const total = cart.reduce((sum, line) => {
    const product = products.find(item => item.id === line.id);
    return sum + product.price * line.qty;
  }, 0);
  return Number.isFinite(total) ? total : null;
}

export function applySaleToInventory(cart, products) {
  if (!validateCartAgainstInventory(cart, products)) return null;
  const updated = products.map(product => {
    const line = cart.find(item => item.id === product.id);
    if (!line) return product;

    const stock = product.stock - line.qty;
    const sold = product.sold + line.qty;
    if (!Number.isFinite(stock) || stock < 0 || !Number.isSafeInteger(sold)) return null;
    return { ...product, stock, sold };
  });
  return updated.some(product => product === null) ? null : updated;
}

export function nextOrderIdentifier(orders) {
  if (!Array.isArray(orders)) return null;
  const ids = orders.map(order => Number(order?.id));
  if (ids.some(id => !Number.isSafeInteger(id) || id < 0)) return null;
  const next = Math.max(1048, ...ids) + 1;
  return Number.isSafeInteger(next) ? next : null;
}
