-- ============================================================================
--  01_pt_user_org_bukrs.sql
--
--  Chuyen PT_USER_ORG tu tham chieu PT_T001.ID sang luu THANG ma don vi (BUKRS).
--  Ly do: danh muc don vi chuan la T001 cua schema APEX (dong bo tu SAP, va la
--  nguon cua H_DATA.BUKRS). PT_T001 la ban nhap tay rieng nen lech dan, lam lop
--  chan h_BUKRS khong dung duoc.
--
--  Chay bang user PT_APP tren schema PT_APP:
--      sqlplus PT_APP/<mat khau>@<tns> @01_pt_user_org_bukrs.sql
--
--  Script IDEMPOTENT: chay lai nhieu lan khong loi, khong ghi de du lieu da co.
--
--  !! DOC TRUOC KHI CHAY !!
--  Backend Java (Tool_Portal Spring Boot) dung chung schema PT_APP va doc
--  PT_USER_ORG.ORG_ID trong ScopeService. Script nay:
--    - KHONG xoa cot ORG_ID, chi cho phep NULL va bo rang buoc FK.
--    - Backend C# van GHI ORG_ID khi ma BUKRS do co trong PT_T001, nen Java
--      tiep tuc chay dung voi cac ma no biet. Chi ma chi co trong T001 (khong co
--      trong PT_T001) moi ra dong BUKRS-only ma Java khong thay.
--  Neu KHONG con dung backend Java thi bo qua doan tren.
--
--  ROLLBACK (neu can quay lai):
--    -- khoi phuc NOT NULL + FK; chi lam duoc khi moi dong deu co ORG_ID
--    ALTER TABLE PT_USER_ORG MODIFY (ORG_ID NUMBER NOT NULL);
--    ALTER TABLE PT_USER_ORG ADD CONSTRAINT FK_PT_USER_ORG_ORG
--          FOREIGN KEY (ORG_ID) REFERENCES PT_T001 (ID);
--    -- cot BUKRS de lai cung vo hai; muon bo:  ALTER TABLE PT_USER_ORG DROP COLUMN BUKRS;
-- ============================================================================

SET SERVEROUTPUT ON
SET DEFINE OFF

PROMPT === 1/5. Them cot BUKRS vao PT_USER_ORG (neu chua co) ===
DECLARE
  n PLS_INTEGER;
BEGIN
  SELECT COUNT(*) INTO n FROM USER_TAB_COLUMNS
   WHERE TABLE_NAME = 'PT_USER_ORG' AND COLUMN_NAME = 'BUKRS';

  IF n = 0 THEN
    EXECUTE IMMEDIATE 'ALTER TABLE PT_USER_ORG ADD (BUKRS VARCHAR2(50))';
    DBMS_OUTPUT.PUT_LINE('  -> da them cot BUKRS');
  ELSE
    DBMS_OUTPUT.PUT_LINE('  -> cot BUKRS da co, bo qua');
  END IF;
END;
/

PROMPT === 2/5. Backfill BUKRS tu ORG_ID hien co ===
UPDATE PT_USER_ORG uo
   SET BUKRS = (SELECT t.BUKRS FROM PT_T001 t WHERE t.ID = uo.ORG_ID)
 WHERE uo.BUKRS IS NULL
   AND uo.ORG_ID IS NOT NULL;

PROMPT === 3/5. Bo rang buoc FK ORG_ID -> PT_T001 (neu con) ===
DECLARE
  CURSOR c IS
    SELECT c.CONSTRAINT_NAME
      FROM USER_CONSTRAINTS c
      JOIN USER_CONS_COLUMNS cc
        ON cc.CONSTRAINT_NAME = c.CONSTRAINT_NAME
     WHERE c.TABLE_NAME = 'PT_USER_ORG'
       AND c.CONSTRAINT_TYPE = 'R'
       AND cc.COLUMN_NAME = 'ORG_ID';
BEGIN
  FOR r IN c LOOP
    EXECUTE IMMEDIATE 'ALTER TABLE PT_USER_ORG DROP CONSTRAINT ' || r.CONSTRAINT_NAME;
    DBMS_OUTPUT.PUT_LINE('  -> da bo constraint ' || r.CONSTRAINT_NAME);
  END LOOP;
END;
/

PROMPT === 4/5. Cho ORG_ID nhan NULL ===
DECLARE
  v_nullable USER_TAB_COLUMNS.NULLABLE%TYPE;
BEGIN
  SELECT NULLABLE INTO v_nullable FROM USER_TAB_COLUMNS
   WHERE TABLE_NAME = 'PT_USER_ORG' AND COLUMN_NAME = 'ORG_ID';

  IF v_nullable = 'N' THEN
    EXECUTE IMMEDIATE 'ALTER TABLE PT_USER_ORG MODIFY (ORG_ID NULL)';
    DBMS_OUTPUT.PUT_LINE('  -> ORG_ID gio nhan NULL');
  ELSE
    DBMS_OUTPUT.PUT_LINE('  -> ORG_ID da nhan NULL, bo qua');
  END IF;
EXCEPTION
  WHEN NO_DATA_FOUND THEN
    DBMS_OUTPUT.PUT_LINE('  -> khong co cot ORG_ID (schema da chuyen xong truoc do)');
END;
/

PROMPT === 5/5. UNIQUE (USER_ID, BUKRS) + index ===
DECLARE
  n PLS_INTEGER;
BEGIN
  -- Rang buoc UNIQUE cu la (USER_ID, ORG_ID); gio khoa nghiep vu la (USER_ID, BUKRS).
  SELECT COUNT(*) INTO n FROM USER_INDEXES WHERE INDEX_NAME = 'UX_PT_USER_ORG_BUKRS';
  IF n = 0 THEN
    BEGIN
      EXECUTE IMMEDIATE
        'CREATE UNIQUE INDEX UX_PT_USER_ORG_BUKRS ON PT_USER_ORG (USER_ID, UPPER(BUKRS))';
      DBMS_OUTPUT.PUT_LINE('  -> da tao UX_PT_USER_ORG_BUKRS');
    EXCEPTION
      WHEN OTHERS THEN
        -- Thuong la ORA-01452: dang co ban ghi trung (USER_ID, BUKRS).
        DBMS_OUTPUT.PUT_LINE('  -> KHONG tao duoc unique index: ' || SQLERRM);
        DBMS_OUTPUT.PUT_LINE('     Kiem trung bang cau duoi roi don truoc khi chay lai:');
        DBMS_OUTPUT.PUT_LINE('     SELECT USER_ID, UPPER(BUKRS), COUNT(*) FROM PT_USER_ORG');
        DBMS_OUTPUT.PUT_LINE('      GROUP BY USER_ID, UPPER(BUKRS) HAVING COUNT(*) > 1;');
    END;
  ELSE
    DBMS_OUTPUT.PUT_LINE('  -> index da co, bo qua');
  END IF;
END;
/

COMMIT;

PROMPT
PROMPT === Ket qua ===
SELECT COUNT(*) AS TONG_DONG,
       COUNT(BUKRS) AS CO_BUKRS,
       COUNT(*) - COUNT(BUKRS) AS THIEU_BUKRS
  FROM PT_USER_ORG;

PROMPT (THIEU_BUKRS > 0 nghia la co dong ORG_ID tro tai ban ghi khong con trong PT_T001)
PROMPT Xem cu the:
PROMPT   SELECT * FROM PT_USER_ORG WHERE BUKRS IS NULL;
