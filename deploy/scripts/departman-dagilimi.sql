-- DEPARTMAN ATAMA DOĞRULAMASI — TEK SORGU.
--
-- Atama öncesi ve sonrası aynı sorgu koşulur; sayılar karşılaştırılır.
-- Üç şeyi birden veriyor: doluluk, dağılım, tarihçe.
--
-- KULLANIM:
--   sudo bash deploy/scripts/departman-dagilimi.sh
with aktif as (
    select p."Id", p."DepartmentId"
    from personnel p
    where p."IsDeleted" = false and p."Status" = 1
)
select
    'ÖZET' as bolum,
    'aktif personel'                       as kalem,
    count(*)::text                          as deger
from aktif
union all
select 'ÖZET', 'departmanı DOLU', count("DepartmentId")::text from aktif
union all
select 'ÖZET', 'departmanı BOŞ',
       (count(*) - count("DepartmentId"))::text from aktif
union all
select 'ÖZET', 'tarihçe satırı',
       (select count(*)::text from personnel_department_history)
union all
select 'ÖZET', 'tarihçesi olan personel',
       (select count(distinct "PersonnelId")::text
        from personnel_department_history)
union all
-- DAĞILIM: her departmanda kaç kişi. Atanmamışlar da bir satır.
select
    'DAĞILIM',
    coalesce(d."Code" || ' · ' || d."Name", '(departmansız)'),
    count(a."Id")::text
from aktif a
left join hr_departments d on d."Id" = a."DepartmentId"
group by d."Code", d."Name"
union all
-- TUTARLILIK: departmanı dolu ama o departman silinmiş/yok olan
-- personel. Sıfırdan büyükse departman silme muhafızı delinmiş demektir.
select
    'TUTARLILIK',
    'departmanı ÇÖZÜLEMEYEN personel',
    count(*)::text
from aktif a
where a."DepartmentId" is not null
  and not exists (
        select 1 from hr_departments d
        where d."Id" = a."DepartmentId" and d."IsDeleted" = false)
order by 1, 3 desc, 2;
