import React, { useState } from 'react';

const demoBusinesses = [
  { id: 'MG-1001', name: 'مجموعه نمونه آلفا', industry: 'خرده‌فروشی', plan: 'Professional', status: 'فعال', branches: 3, users: 18 },
  { id: 'MG-1002', name: 'کافه نمونه بتا', industry: 'کافه و رستوران', plan: 'Business', status: 'در انتظار راه‌اندازی', branches: 1, users: 6 },
  { id: 'MG-1003', name: 'مرکز خدمات گاما', industry: 'خدمات', plan: 'Starter', status: 'معلق', branches: 2, users: 9 },
];
const nav = [
  ['overview', '◫', 'نمای کلی پلتفرم'],
  ['businesses', '▦', 'کسب‌وکارها'],
  ['modules', '◈', 'ماژول‌ها و مجوزها'],
  ['plans', '◇', 'طرح‌ها و اشتراک‌ها'],
  ['databases', '▤', 'پایگاه‌داده‌ها'],
  ['security', '⬡', 'امنیت و دسترسی'],
  ['operations', '⌁', 'سلامت و عملیات'],
  ['audit', '☷', 'گزارش رویدادها'],
];
const sections = {
  overview: ['نمای کلی پلتفرم', 'وضعیت سراسری زیرساخت و کسب‌وکارهای MG'],
  businesses: ['مدیریت کسب‌وکارها', 'ثبت، بررسی و مدیریت چرخه عمر هر کسب‌وکار'],
  modules: ['ماژول‌ها و مجوزها', 'کاتالوگ امکانات و دسترسی هر طرح'],
  plans: ['طرح‌ها و اشتراک‌ها', 'مدیریت پلن‌ها، محدودیت‌ها و تمدیدها'],
  databases: ['پایگاه‌داده‌ها', 'وضعیت پایگاه‌داده مرکزی و پایگاه‌های مستقل'],
  security: ['امنیت و دسترسی', 'مدیریت اپراتورهای سازنده و دسترسی‌های ممتاز'],
  operations: ['سلامت و عملیات', 'پایش سرویس‌ها، صف‌ها و پشتیبان‌گیری'],
  audit: ['گزارش رویدادها', 'ردیابی تغییرات مدیریتی و رویدادهای امنیتی'],
};
function CreatorConsole() {
  const [page, setPage] = useState('overview');
  const [query, setQuery] = useState('');
  const title = sections[page];
  const rows = demoBusinesses.filter(x => (x.name + x.id + x.industry).includes(query));
  return <div className="creator-shell">
    <aside className="creator-sidebar">
      <div className="creator-brand"><div className="creator-mark">MG</div><div><b>MG CREATOR</b><small>PLATFORM CONTROL PLANE</small></div></div>
      <div className="creator-secure"><i/> محیط مدیریتی سازنده <small>دسترسی سطح پلتفرم</small></div>
      <nav>{nav.map(([id, icon, label]) => <button key={id} className={page===id?'selected':''} onClick={()=>setPage(id)}><span>{icon}</span>{label}</button>)}</nav>
      <div className="creator-sidebar-foot"><span className="creator-avatar">MG</span><div><b>مالک پلتفرم</b><small>Creator Owner</small></div><span className="creator-online">●</span></div>
    </aside>
    <main className="creator-main">
      <header className="creator-topbar"><div><span>MG PLATFORM</span><b> / </b><strong>{title[0]}</strong></div><div className="creator-top-actions"><span className="creator-preview">پیش‌نمایش رابط</span><a href="/">ورود به فضای کسب‌وکار ↗</a></div></header>
      <section className="creator-content">
        <div className="creator-heading"><div><small>MG CREATOR CONTROL CENTER</small><h1>{title[0]}</h1><p>{title[1]}</p></div><div className="creator-date">وضعیت نمایشی · نسخه توسعه</div></div>
        {page==='overview' ? <>
          <div className="creator-alert"><b>محیط نمایشی پنل سازنده</b><span>اطلاعات این صفحه نمونه است و هنوز به API و پایگاه‌داده مرکزی متصل نشده است.</span></div>
          <div className="creator-metrics">
            <Metric label="کسب‌وکارهای ثبت‌شده" value="۱۲۸" note="نمونه آماری" icon="▦"/>
            <Metric label="کسب‌وکارهای فعال" value="۱۱۴" note="۸۹٪ از کل" icon="↗"/>
            <Metric label="کاربران کسب‌وکارها" value="۲٬۴۸۶" note="حساب‌های نمونه" icon="♙"/>
            <Metric label="ماژول‌های قابل ارائه" value="۱۸" note="کاتالوگ نمایشی" icon="◈"/>
          </div>
          <div className="creator-grid">
            <section className="creator-panel"><div className="creator-panel-head"><div><h2>وضعیت سرویس‌های اصلی</h2><p>نمایش سلامت اجزای زیرساخت</p></div><span className="creator-demo-tag">نمونه</span></div>
              {['MG Control Plane API','Business API Gateway','PostgreSQL — Central','Tenant Database Router','Background Workers','Object Storage'].map((name,i)=><div className="service-row" key={name}><span className="service-dot" style={{opacity:i===4?.45:1}}/><b>{name}</b><small>{i===4?'در حال بررسی':'نمایشی'}</small></div>)}
            </section>
            <section className="creator-panel"><div className="creator-panel-head"><div><h2>توزیع طرح‌های اشتراک</h2><p>تقسیم‌بندی نمونه مشتریان</p></div></div>
              {[['Starter',32],['Professional',58],['Business',76],['Enterprise',24]].map(([name,value])=><div className="plan-meter" key={name}><div><b>{name}</b><small>{value}%</small></div><i><span style={{width:value+'%'}}/></i></div>)}
            </section>
          </div>
          <BusinessTable rows={demoBusinesses.slice(0,3)}/>
        </> : page==='businesses' ? <>
          <div className="creator-toolbar"><input value={query} onChange={e=>setQuery(e.target.value)} placeholder="جستجوی نام، شناسه یا صنعت کسب‌وکار..."/><button disabled title="اتصال به API در حال پیاده‌سازی است">＋ ثبت کسب‌وکار</button></div>
          <BusinessTable rows={rows}/>
        </> : <section className="creator-panel creator-placeholder"><div className="creator-placeholder-icon">◇</div><h2>{title[0]}</h2><p>ساختار این بخش در معماری هسته سازنده تعریف شده است. اتصال عملیاتی، سیاست‌های دسترسی و داده‌های واقعی در مراحل بعدی پیاده‌سازی می‌شوند.</p><span>این بخش هنوز عملیاتی نیست</span></section>}
      </section>
      <footer className="creator-footer">MG BUSINESS PLATFORM <span>•</span> پنل محرمانه سازنده · پیش‌نمایش رابط کاربری</footer>
    </main>
  </div>;
}
function Metric({label,value,note,icon}){return <div className="creator-metric"><div><span>{label}</span><i>{icon}</i></div><b>{value}</b><small>{note}</small></div>}
function BusinessTable({rows}){return <section className="creator-panel"><div className="creator-panel-head"><div><h2>آخرین کسب‌وکارها</h2><p>نمونه‌ای از فهرست tenantهای پلتفرم</p></div><span className="creator-demo-tag">داده آزمایشی</span></div><div className="creator-table-wrap"><table><thead><tr><th>شناسه</th><th>کسب‌وکار</th><th>حوزه فعالیت</th><th>طرح</th><th>شعب</th><th>کاربران</th><th>وضعیت</th></tr></thead><tbody>{rows.length?rows.map(x=><tr key={x.id}><td>{x.id}</td><td><b>{x.name}</b></td><td>{x.industry}</td><td>{x.plan}</td><td>{x.branches}</td><td>{x.users}</td><td><span className={'creator-status '+(x.status==='فعال'?'active':x.status==='معلق'?'paused':'pending')}>{x.status}</span></td></tr>):<tr><td colSpan="7">موردی یافت نشد.</td></tr>}</tbody></table></div></section>}
export default CreatorConsole;
