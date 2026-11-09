const money = new Intl.NumberFormat('en-IE', { style: 'currency', currency: 'EUR', maximumFractionDigits: 0 });
const dateTime = new Intl.DateTimeFormat('en-GB', { dateStyle: 'medium', timeStyle: 'short' });
const date = new Intl.DateTimeFormat('en-GB', { dateStyle: 'medium' });

export const formatMoney = (amount: number, currency = 'EUR'): string =>
  currency === 'EUR' ? money.format(amount) : `${amount.toFixed(0)} ${currency}`;

export const formatDateTime = (iso: string): string => dateTime.format(new Date(iso));

export const formatDate = (isoDate: string): string => date.format(new Date(`${isoDate}T00:00:00`));

/** "EligibilityAssessed" → "Eligibility assessed" */
export const humanizeEventType = (type: string): string =>
  type.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, (c) => c.toUpperCase()).replace(/ (\w)/g, (_, c: string) => ` ${c.toLowerCase()}`);
