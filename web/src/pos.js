export function validateCartAgainstInventory(cart, products) {
  if (!Array.isArray(cart) || cart.length === 0 || !Array.isArray(products)) return false;
  return cart.every(line => {
    if (!line || !Number.isInteger(line.qty) || line.qty < 1) return false;
    const product = products.find(item => item.id === line.id);
    return Boolean(product && Number.isFinite(product.stock) && product.stock >= line.qty
      && Number.isFinite(product.price) && product.price >= 0);
  });
}

export function calculateCartTotal(cart, products) {
  if (!validateCartAgainstInventory(cart, products)) return null;
  return cart.reduce((total, line) => {
    const product = products.find(item => item.id === line.id);
    return total + product.price * line.qty;
  }, 0);
}

export function applySaleToInventory(cart, products) {
  if (!validateCartAgainstInventory(cart, products)) return null;
  return products.map(product => {
    const line = cart.find(item => item.id === product.id);
    return line
      ? { ...product, stock: product.stock - line.qty, sold: product.sold + line.qty }
      : product;
  });
}

export function nextOrderIdentifier(orders) {
  if (!Array.isArray(orders)) return null;
  const ids = orders.map(order => Number(order?.id));
  if (ids.some(id => !Number.isSafeInteger(id) || id < 0)) return null;
  return Math.max(1048, ...ids) + 1;
}
