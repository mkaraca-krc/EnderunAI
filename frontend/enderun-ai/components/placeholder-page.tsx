import { AppShell } from "./app-shell";
import { PageHeader } from "./page-header";

export function PlaceholderPage({
  active,
  title,
  description,
}: {
  active: string;
  title: string;
  description: string;
}) {
  return (
    <AppShell active={active}>
      <PageHeader title={title} description={description} eyebrow="Enderun AI" />
      <section className="mt-8 rounded-3xl border border-cyan-400/20 bg-gradient-to-br from-cyan-500/15 via-blue-600/5 to-transparent p-8">
        <p className="text-sm font-semibold text-cyan-300">MODÜL HAZIRLANIYOR</p>
        <h3 className="mt-3 text-3xl font-bold">{title}</h3>
        <p className="mt-4 max-w-2xl leading-7 text-slate-300">
          {description}. Bu sayfa Enderun AI veri tabanı ve yetkilendirme sistemiyle
          bağlanacak şekilde hazırlanacaktır.
        </p>
      </section>
    </AppShell>
  );
}
