import {
    Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell,
    WidthType, AlignmentType, BorderStyle, HeadingLevel, ShadingType,
    convertInchesToTwip, TableBorders, VerticalAlign
} from 'docx';
import fs from 'fs';

// ═══════════════════════════════════════════════════════════
// Helper Functions
// ═══════════════════════════════════════════════════════════

function heading(text, level = HeadingLevel.HEADING_1) {
    return new Paragraph({ heading: level, spacing: { before: 300, after: 150 }, children: [new TextRun({ text, bold: true })] });
}

function subHeading(text) {
    return heading(text, HeadingLevel.HEADING_2);
}

function toolHeading(text) {
    return new Paragraph({
        heading: HeadingLevel.HEADING_2,
        spacing: { before: 400, after: 100 },
        shading: { type: ShadingType.SOLID, color: '1F4E79' },
        children: [new TextRun({ text, bold: true, color: 'FFFFFF', size: 28 })]
    });
}

function sectionTitle(text) {
    return new Paragraph({
        heading: HeadingLevel.HEADING_3,
        spacing: { before: 250, after: 100 },
        children: [new TextRun({ text, bold: true, color: '1F4E79', size: 24 })]
    });
}

function normalText(text, opts = {}) {
    return new Paragraph({
        spacing: { after: 80 },
        children: [new TextRun({ text, size: 22, ...opts })]
    });
}

function bulletItem(text) {
    return new Paragraph({
        bullet: { level: 0 },
        spacing: { after: 60 },
        children: [new TextRun({ text, size: 22 })]
    });
}

function numberedQuestion(num, text) {
    return new Paragraph({
        spacing: { before: 150, after: 60 },
        children: [
            new TextRun({ text: `${num}. `, bold: true, size: 22, color: '1F4E79' }),
            new TextRun({ text, size: 22 })
        ]
    });
}

function helpText(text) {
    return new Paragraph({
        spacing: { after: 60 },
        indent: { left: convertInchesToTwip(0.3) },
        children: [new TextRun({ text: `💡 ${text}`, size: 20, italics: true, color: '666666' })]
    });
}

function answerBox() {
    return new Paragraph({
        spacing: { after: 120 },
        indent: { left: convertInchesToTwip(0.3) },
        border: {
            bottom: { style: BorderStyle.DOTTED, size: 1, color: '999999' }
        },
        children: [new TextRun({ text: '📝 Trả lời: ', size: 22, color: '999999' })]
    });
}

function answerBoxMultiLine(lines = 3) {
    const items = [];
    items.push(new Paragraph({
        spacing: { after: 20 },
        indent: { left: convertInchesToTwip(0.3) },
        children: [new TextRun({ text: '📝 Trả lời:', size: 22, color: '999999' })]
    }));
    for (let i = 0; i < lines; i++) {
        items.push(new Paragraph({
            spacing: { after: 30 },
            indent: { left: convertInchesToTwip(0.3) },
            border: { bottom: { style: BorderStyle.DOTTED, size: 1, color: 'CCCCCC' } },
            children: [new TextRun({ text: ' ', size: 22 })]
        }));
    }
    return items;
}

function checkboxList(items) {
    return items.map(item => new Paragraph({
        spacing: { after: 40 },
        indent: { left: convertInchesToTwip(0.3) },
        children: [new TextRun({ text: `☐ ${item}`, size: 22 })]
    }));
}

function separator() {
    return new Paragraph({
        spacing: { before: 200, after: 200 },
        border: { bottom: { style: BorderStyle.SINGLE, size: 2, color: '1F4E79' } },
        children: [new TextRun({ text: '' })]
    });
}

function infoTable(rows) {
    const noBorders = {
        top: { style: BorderStyle.NONE, size: 0 },
        bottom: { style: BorderStyle.NONE, size: 0 },
        left: { style: BorderStyle.NONE, size: 0 },
        right: { style: BorderStyle.NONE, size: 0 }
    };
    return new Table({
        width: { size: 100, type: WidthType.PERCENTAGE },
        borders: {
            top: { style: BorderStyle.SINGLE, size: 1, color: 'CCCCCC' },
            bottom: { style: BorderStyle.SINGLE, size: 1, color: 'CCCCCC' },
            left: { style: BorderStyle.SINGLE, size: 1, color: 'CCCCCC' },
            right: { style: BorderStyle.SINGLE, size: 1, color: 'CCCCCC' },
            insideHorizontal: { style: BorderStyle.SINGLE, size: 1, color: 'CCCCCC' },
            insideVertical: { style: BorderStyle.SINGLE, size: 1, color: 'CCCCCC' }
        },
        rows: rows.map((row, idx) => new TableRow({
            children: row.map((cell, cellIdx) => new TableCell({
                width: { size: cellIdx === 0 ? 30 : 70, type: WidthType.PERCENTAGE },
                shading: idx === 0 ? { type: ShadingType.SOLID, color: '1F4E79' } : undefined,
                verticalAlign: VerticalAlign.CENTER,
                children: [new Paragraph({
                    spacing: { before: 40, after: 40 },
                    children: [new TextRun({
                        text: cell,
                        bold: idx === 0,
                        size: 22,
                        color: idx === 0 ? 'FFFFFF' : '333333'
                    })]
                })]
            }))
        }))
    });
}

// ═══════════════════════════════════════════════════════════
// Build Document
// ═══════════════════════════════════════════════════════════

const doc = new Document({
    styles: {
        default: {
            document: {
                run: { font: 'Segoe UI', size: 22 }
            }
        }
    },
    sections: [{
        properties: {
            page: {
                margin: {
                    top: convertInchesToTwip(0.8),
                    bottom: convertInchesToTwip(0.8),
                    left: convertInchesToTwip(0.9),
                    right: convertInchesToTwip(0.9)
                }
            }
        },
        children: [
            // ═══ TITLE PAGE ═══
            new Paragraph({ spacing: { before: 600 }, children: [] }),
            new Paragraph({
                alignment: AlignmentType.CENTER,
                spacing: { after: 100 },
                children: [new TextRun({ text: 'CIC BIM ADDIN', bold: true, size: 40, color: '1F4E79' })]
            }),
            new Paragraph({
                alignment: AlignmentType.CENTER,
                spacing: { after: 200 },
                children: [new TextRun({ text: 'Thu thập Thông tin Kỹ thuật', bold: true, size: 32, color: '333333' })]
            }),
            new Paragraph({
                alignment: AlignmentType.CENTER,
                spacing: { after: 100 },
                children: [new TextRun({ text: '5 TOOLS ƯU TIÊN PHÁT TRIỂN', bold: true, size: 28, color: '1F4E79' })]
            }),
            separator(),
            new Paragraph({
                alignment: AlignmentType.CENTER,
                spacing: { after: 50 },
                children: [new TextRun({ text: '📅 Ngày gửi: ___/___/2026', size: 22, color: '666666' })]
            }),
            new Paragraph({
                alignment: AlignmentType.CENTER,
                spacing: { after: 50 },
                children: [new TextRun({ text: '👤 Người điền: ________________________________________', size: 22, color: '666666' })]
            }),
            new Paragraph({
                alignment: AlignmentType.CENTER,
                spacing: { after: 50 },
                children: [new TextRun({ text: '🏢 Bộ phận: ☐ KC   ☐ KT   ☐ MEP   ☐ Khác: _________', size: 22, color: '666666' })]
            }),
            new Paragraph({
                alignment: AlignmentType.CENTER,
                spacing: { after: 300 },
                children: [new TextRun({ text: '📂 Revit Version: ☐ 2022   ☐ 2023   ☐ 2024   ☐ 2025', size: 22, color: '666666' })]
            }),

            // ═══ GIỚI THIỆU ═══
            new Paragraph({
                shading: { type: ShadingType.SOLID, color: 'E8F0FE' },
                spacing: { after: 100 },
                children: [new TextRun({
                    text: '📋 HƯỚNG DẪN ĐIỀN THÔNG TIN',
                    bold: true, size: 24, color: '1F4E79'
                })]
            }),
            normalText('Tài liệu này thu thập thông tin kỹ thuật cần thiết để phát triển 5 tools automation cho CIC BIM Addin.'),
            bulletItem('Trả lời chi tiết nhất có thể — mô tả workflow thực tế bạn đang làm.'),
            bulletItem('Nếu câu hỏi không liên quan đến công việc của bạn → ghi "Không liên quan".'),
            bulletItem('Đính kèm file mẫu (.rvt, .dwg, .xlsx) nếu được yêu cầu — rất quan trọng!'),
            bulletItem('Screenshot / video quy trình thủ công hiện tại là tài liệu cực kỳ giá trị.'),
            separator(),

            // ═══ DANH SÁCH 5 TOOLS ═══
            sectionTitle('DANH SÁCH 5 TOOLS ƯU TIÊN'),
            new Paragraph({ spacing: { after: 150 }, children: [] }),
            infoTable([
                ['#', 'Mã', 'Tên Tool', 'Nhóm'],
                ['1', 'B3.2', 'Calculate Formwork — Thống kê ván khuôn', 'KC'],
                ['2', 'B1.10', 'Plaster — Tạo lớp trát tự động', 'KT'],
                ['3', 'B1.23', 'Chia khúc ống — Chia ống theo kích thước NSX', 'MEP'],
                ['4', 'B1.24', 'Pipe từ CAD — Dựng pipe từ line type CAD', 'MEP'],
                ['5', 'B1.28', 'Thay đổi độ dốc ống', 'MEP']
            ]),
            separator(),

            // ═════════════════════════════════════════════════════
            // TOOL 1: CALCULATE FORMWORK
            // ═════════════════════════════════════════════════════
            toolHeading('  🔧 TOOL 1: B3.2 — CALCULATE FORMWORK (THỐNG KÊ VÁN KHUÔN)  '),
            normalText('Nhóm liên quan: KC (Kết cấu)', { italics: true, color: '666666' }),
            normalText('Mô tả: Tự động tính toán diện tích ván khuôn cho các cấu kiện kết cấu trong model Revit.'),

            sectionTitle('A. Phạm vi cấu kiện'),
            numberedQuestion(1, 'Loại cấu kiện nào cần tính ván khuôn? (tick vào các ô phù hợp)'),
            ...checkboxList(['Dầm (Beam)', 'Cột (Column)', 'Tường (Wall)', 'Sàn (Floor/Slab)', 'Móng (Foundation)', 'Cầu thang (Stair)', 'Khác: ___________________']),

            sectionTitle('B. Quy tắc tính diện tích'),
            numberedQuestion(2, 'Với DẦM — tính diện tích ván khuôn gồm những mặt nào?'),
            ...checkboxList(['2 mặt bên + đáy dầm', 'Có trừ phần giao dầm-cột', 'Có trừ phần dầm tiếp xúc với sàn', 'Quy tắc khác: ___________________']),
            ...answerBoxMultiLine(2),

            numberedQuestion(3, 'Với CỘT — tính diện tích ván khuôn gồm những mặt nào?'),
            ...checkboxList(['4 mặt (cột vuông/chữ nhật)', 'Chu vi × chiều cao (cột tròn)', 'Có trừ phần giao cột-dầm', 'Có trừ phần giao cột-sàn', 'Quy tắc khác: ___________________']),
            ...answerBoxMultiLine(2),

            numberedQuestion(4, 'Với TƯỜNG — tính diện tích ván khuôn thế nào?'),
            helpText('VD: 2 mặt × cao × dài? Có trừ lỗ cửa? Có trừ phần giao sàn?'),
            ...answerBoxMultiLine(2),

            numberedQuestion(5, 'Với MÓNG — tính diện tích ván khuôn thế nào?'),
            helpText('VD: Móng đơn = 4 mặt bên? Móng băng = 2 mặt dài + 2 mặt đầu? Có trừ mặt tiếp đất?'),
            ...answerBoxMultiLine(2),

            numberedQuestion(6, 'Cấu kiện ĐẶC BIỆT: Dầm console, dầm cong, lanh tô — có tính không? Quy tắc riêng?'),
            ...answerBoxMultiLine(2),

            sectionTitle('C. Kết quả đầu ra'),
            numberedQuestion(7, 'Đơn vị kết quả: m² hay đơn vị khác?'),
            answerBox(),

            numberedQuestion(8, 'Cần nhóm kết quả theo tiêu chí nào?'),
            ...checkboxList(['Theo Level (tầng)', 'Theo loại cấu kiện', 'Theo Zone / Area', 'Theo Mark cấu kiện', 'Khác: ___________________']),

            numberedQuestion(9, 'Format bảng kết quả mong muốn — mô tả hoặc đính kèm file Excel mẫu:'),
            ...answerBoxMultiLine(3),

            sectionTitle('D. File mẫu cần cung cấp'),
            ...checkboxList([
                'File .rvt mẫu có cấu kiện KC thực tế (dầm, cột, sàn, móng)',
                'File Excel mẫu thể hiện format kết quả ván khuôn mong muốn',
                'Screenshot bản vẽ thể hiện cách đo ván khuôn thủ công'
            ]),

            separator(),

            // ═════════════════════════════════════════════════════
            // TOOL 2: PLASTER
            // ═════════════════════════════════════════════════════
            toolHeading('  🧱 TOOL 2: B1.10 — PLASTER (TẠO LỚP TRÁT TỰ ĐỘNG)  '),
            normalText('Nhóm liên quan: KT (Kiến trúc)', { italics: true, color: '666666' }),
            normalText('Mô tả: Tự động tạo lớp trát tường, cột, dầm, sàn trong room được chọn (áp dụng cả file link).'),

            sectionTitle('A. Chiều dày lớp trát'),
            numberedQuestion(1, 'Chiều dày lớp trát chuẩn cho từng loại cấu kiện:'),
            infoTable([
                ['Loại cấu kiện', 'Chiều dày (mm)'],
                ['Tường trong', ''],
                ['Tường ngoài', ''],
                ['Cột', ''],
                ['Dầm (mặt dưới)', ''],
                ['Dầm (mặt bên)', ''],
                ['Sàn (mặt dưới/trần)', '']
            ]),

            numberedQuestion(2, 'Chiều dày có thay đổi theo dự án không? Hay dùng 1 giá trị cố định?'),
            answerBox(),

            sectionTitle('B. Wall Type & Material'),
            numberedQuestion(3, 'Wall Type / Floor Type dùng cho lớp trát tên là gì? (nếu đã có trong template)'),
            helpText('VD: Wall_Plaster_15mm, Floor_Plaster_20mm — hoặc để tool tự tạo'),
            answerBox(),

            numberedQuestion(4, 'Material (vật liệu) lớp trát dùng tên gì trong model?'),
            helpText('VD: Vữa trát xi măng, Plaster - Cement Mortar...'),
            answerBox(),

            sectionTitle('C. Phạm vi áp dụng'),
            numberedQuestion(5, 'Phạm vi tạo lớp trát:'),
            ...checkboxList(['Theo Room được chọn', 'Toàn bộ Room trong 1 Level', 'Toàn bộ Room trong model', 'Khác: ___________________']),

            numberedQuestion(6, 'Trát mặt nào của tường?'),
            ...checkboxList(['Chỉ mặt trong Room', 'Cả 2 mặt tường', 'Tùy chọn khi chạy tool']),

            numberedQuestion(7, 'Xử lý cửa (Door / Window):'),
            ...checkboxList(['Lớp trát tự cắt quanh cửa', 'Lớp trát phủ qua (user tự cắt)', 'Tạo thêm lớp trát bao quanh khuôn cửa (laibô cửa)']),

            numberedQuestion(8, 'Có trát quanh cấu kiện từ file KC link (cột KC, dầm KC) không?'),
            ...checkboxList(['Có — tool cần đọc geometry từ file link', 'Không — chỉ trát tường/sàn trong file hiện tại']),

            sectionTitle('D. Yêu cầu bổ sung'),
            numberedQuestion(9, 'Có cần tạo lớp len đá chân tường (skirting) kèm theo không?'),
            answerBox(),

            numberedQuestion(10, 'Có cần tạo lớp sơn bên ngoài lớp trát không?'),
            answerBox(),

            sectionTitle('E. File mẫu cần cung cấp'),
            ...checkboxList([
                'File .rvt mẫu có Room đã đặt sẵn',
                'Screenshot / PDF bản vẽ thể hiện lớp trát mong muốn',
                'Template dự án (.rte) có sẵn Wall Type lớp trát (nếu có)'
            ]),

            separator(),

            // ═════════════════════════════════════════════════════
            // TOOL 3: CHIA KHÚC ỐNG
            // ═════════════════════════════════════════════════════
            toolHeading('  🔩 TOOL 3: B1.23 — CHIA KHÚC ỐNG  '),
            normalText('Nhóm liên quan: MEP (Cơ điện / Cấp thoát nước)', { italics: true, color: '666666' }),
            normalText('Mô tả: Chia ống ngầm thành khúc theo kích thước nhà sản xuất + đặt gối đỡ tự động.'),

            sectionTitle('A. Loại ống & Chiều dài khúc'),
            numberedQuestion(1, 'Loại ống nào cần chia khúc?'),
            ...checkboxList(['Ống PCCC (Fire Protection)', 'Ống cấp nước (Domestic Water)', 'Ống thoát nước (Sanitary)', 'Ống nước mưa (Storm)', 'Tất cả ống nằm ngang', 'Khác: ___________________']),

            numberedQuestion(2, 'Bảng chiều dài khúc ống theo đường kính — điền vào bảng sau:'),
            infoTable([
                ['Đường kính ống', 'Chiều dài 1 khúc (m)', 'Loại ống (thép/PVC/gang...)'],
                ['DN50', '', ''],
                ['DN65', '', ''],
                ['DN80', '', ''],
                ['DN100', '', ''],
                ['DN150', '', ''],
                ['DN200', '', ''],
                ['DN250', '', ''],
                ['DN300', '', ''],
                ['Khác: ______', '', '']
            ]),

            numberedQuestion(3, 'Có catalog / tài liệu nhà sản xuất không? Nếu có, đính kèm file.'),
            answerBox(),

            sectionTitle('B. Fitting & Gối đỡ'),
            numberedQuestion(4, 'Fitting nối giữa các khúc ống dùng loại nào?'),
            ...checkboxList(['Coupling (măng xông)', 'Mối hàn', 'Mặt bích (Flange)', 'Ren (Threaded)', 'Khác: ___________________']),

            numberedQuestion(5, 'Có cần đặt gối đỡ (pipe support/hanger) tự động không?'),
            ...checkboxList(['Có — đặt tại mỗi mối nối', 'Có — đặt theo khoảng cách cố định', 'Không cần']),

            numberedQuestion(6, 'Nếu có gối đỡ — khoảng cách giữa các gối đỡ là bao nhiêu?'),
            answerBox(),

            numberedQuestion(7, 'Family gối đỡ đang dùng tên gì? Đã có trong model chưa?'),
            answerBox(),

            sectionTitle('C. Xử lý đoạn lẻ'),
            numberedQuestion(8, 'Đoạn cuối cùng (< 1 khúc) xử lý thế nào?'),
            ...checkboxList(['Để nguyên đoạn lẻ', 'Ghép 2 đoạn ngắn thay vì 1 dài + 1 lẻ', 'Tùy chọn khi chạy tool', 'Khác: ___________________']),

            sectionTitle('D. File mẫu cần cung cấp'),
            ...checkboxList([
                'File .rvt mẫu có hệ thống ống đã dựng',
                'Catalog / bảng thông số ống từ nhà sản xuất',
                'Family gối đỡ (.rfa) nếu đã có'
            ]),

            separator(),

            // ═════════════════════════════════════════════════════
            // TOOL 4: PIPE TỪ CAD LINE TYPE
            // ═════════════════════════════════════════════════════
            toolHeading('  🔌 TOOL 4: B1.24 — PIPE TỪ CAD LINE TYPE  '),
            normalText('Nhóm liên quan: MEP (PCCC / Cấp thoát nước)', { italics: true, color: '666666' }),
            normalText('Mô tả: Dựng pipe tự động từ line type trong file CAD link, nhập cao độ và các thông số.'),

            sectionTitle('A. Bảng mapping Line Type CAD → Pipe Type Revit'),
            numberedQuestion(1, 'Liệt kê tất cả Line Type CAD đang dùng và Pipe Type Revit tương ứng:'),
            helpText('Điền đầy đủ nhất có thể. Tool sẽ dùng bảng này để tự động mapping.'),
            infoTable([
                ['Line Type CAD', 'Pipe Type Revit', 'Size (DN)', 'System Type'],
                ['', '', '', ''],
                ['', '', '', ''],
                ['', '', '', ''],
                ['', '', '', ''],
                ['', '', '', ''],
                ['', '', '', ''],
                ['', '', '', ''],
                ['', '', '', ''],
                ['', '', '', ''],
                ['', '', '', ''],
                ['', '', '', '']
            ]),

            sectionTitle('B. Cao độ & Thông số'),
            numberedQuestion(2, 'Cao độ (Elevation) ống được xác định thế nào?'),
            ...checkboxList([
                'Nhập 1 cao độ chung cho tất cả ống',
                'Mỗi hệ thống (system) có cao độ riêng',
                'Mỗi đoạn ống có cao độ riêng',
                'Lấy từ text/annotation trong CAD'
            ]),

            numberedQuestion(3, 'Cao độ tính theo mốc nào?'),
            ...checkboxList(['BOD — Bottom of Pipe (đáy ống)', 'CL — Center Line (tim ống)', 'TOD — Top of Pipe (đỉnh ống)']),

            numberedQuestion(4, 'System Type trong Revit mà ống thuộc về:'),
            helpText('VD: Fire Protection - Wet, Domestic Cold Water, Sanitary...'),
            answerBox(),

            sectionTitle('C. Xử lý hình học'),
            numberedQuestion(5, 'Line CAD có đoạn cong (arc) không? Hay chỉ có line thẳng + góc?'),
            answerBox(),

            numberedQuestion(6, 'Sau khi dựng pipe, có cần tự động nối fitting (tê, co, giảm) không?'),
            ...checkboxList(['Có — tự nối tất cả fitting', 'Không — chỉ dựng pipe thẳng', 'Tùy chọn khi chạy tool']),

            sectionTitle('D. Layer & Filter CAD'),
            numberedQuestion(7, 'Ống cần dựng nằm trên layer CAD nào? Liệt kê:'),
            helpText('VD: M-PIPE-PCCC, M-PIPE-WATER, E-FIRE-SPRINKLER...'),
            ...answerBoxMultiLine(3),

            numberedQuestion(8, 'Có quy ước màu sắc / linetype trong CAD cho từng loại ống không?'),
            answerBox(),

            sectionTitle('E. File mẫu cần cung cấp'),
            ...checkboxList([
                'File .dwg (CAD) mẫu có hệ thống ống đã vẽ',
                'File .rvt mẫu đã link file CAD trên',
                'Bảng quy ước layer / line type CAD của dự án (nếu có)'
            ]),

            separator(),

            // ═════════════════════════════════════════════════════
            // TOOL 5: THAY ĐỔI ĐỘ DỐC ỐNG
            // ═════════════════════════════════════════════════════
            toolHeading('  📐 TOOL 5: B1.28 — THAY ĐỔI ĐỘ DỐC ỐNG  '),
            normalText('Nhóm liên quan: MEP (Cấp thoát nước)', { italics: true, color: '666666' }),
            normalText('Mô tả: Nhập độ dốc → pipe fitting tự động xoay theo góc tương ứng.'),

            sectionTitle('A. Loại ống áp dụng'),
            numberedQuestion(1, 'Loại ống nào cần thay đổi độ dốc?'),
            ...checkboxList(['Ống thoát sàn (Sanitary)', 'Ống nước mưa (Storm)', 'Ống PCCC (nếu cần)', 'Tất cả ống nằm ngang', 'Khác: ___________________']),

            sectionTitle('B. Cách nhập & Hướng dốc'),
            numberedQuestion(2, 'Cách nhập giá trị độ dốc:'),
            ...checkboxList(['Phần trăm (VD: 1%, 2%)', 'Tỷ lệ (VD: 1/100, 1/50)', 'Góc (VD: 0.57°)', 'Cả phần trăm và tỷ lệ']),

            numberedQuestion(3, 'Hướng dốc xác định thế nào?'),
            ...checkboxList([
                'Tự xác định theo Flow Direction trong Revit',
                'User chỉ định đầu cao / đầu thấp',
                'Luôn dốc về phía ống đứng / hố ga gần nhất',
                'Khác: ___________________'
            ]),

            sectionTitle('C. Phạm vi & Fitting'),
            numberedQuestion(4, 'Phạm vi thay đổi:'),
            ...checkboxList([
                'Chỉ 1 đoạn ống được chọn',
                'Toàn bộ ống nối liền nhau (connected pipe run)',
                'Toàn bộ ống trong 1 system',
                'Tùy chọn khi chạy tool'
            ]),

            numberedQuestion(5, 'Khi thay đổi dốc, pipe fitting (co, tê) có tự xoay theo không?'),
            ...checkboxList(['Có — fitting phải auto-rotate', 'Không — chỉ thay dốc ống, fitting giữ nguyên']),

            sectionTitle('D. Giá trị dốc phổ biến'),
            numberedQuestion(6, 'Bảng giá trị dốc tiêu chuẩn đang dùng:'),
            infoTable([
                ['Loại ống', 'Độ dốc tiêu chuẩn'],
                ['Ống thoát sàn', ''],
                ['Ống thoát ngoài nhà', ''],
                ['Ống nước mưa', ''],
                ['Ống khác: ______', '']
            ]),

            sectionTitle('E. File mẫu cần cung cấp'),
            ...checkboxList([
                'File .rvt mẫu có hệ thống ống thoát nước đã dựng',
                'Screenshot thể hiện kết quả mong muốn sau khi thay dốc'
            ]),

            separator(),

            // ═══════════════════════════════════════════════════
            // YÊU CẦU CHUNG
            // ═══════════════════════════════════════════════════
            toolHeading('  📎 YÊU CẦU CHUNG — ÁP DỤNG CHO TẤT CẢ 5 TOOLS  '),

            sectionTitle('A. Môi trường làm việc'),
            numberedQuestion(1, 'Phiên bản Revit chính đang dùng (chọn 1):'),
            ...checkboxList(['Revit 2022', 'Revit 2023', 'Revit 2024', 'Revit 2025']),

            numberedQuestion(2, 'Template dự án (.rte) hiện tại có sẵn không? Nếu có — đính kèm.'),
            answerBox(),

            numberedQuestion(3, 'File Shared Parameter (.txt) đang dùng? Đính kèm nếu có.'),
            answerBox(),

            sectionTitle('B. File đính kèm checklist'),
            normalText('Đánh dấu các file bạn đã đính kèm:'),
            ...checkboxList([
                'File .rvt mẫu (cho Tool 1: Formwork)',
                'File .rvt mẫu (cho Tool 2: Plaster — có Room)',
                'File .rvt mẫu (cho Tool 3: Chia khúc ống)',
                'File .dwg mẫu (cho Tool 4: Pipe từ CAD)',
                'File .rvt đã link CAD (cho Tool 4)',
                'File .rvt mẫu (cho Tool 5: Độ dốc ống)',
                'File Excel mẫu format kết quả (cho Tool 1)',
                'Catalog nhà sản xuất ống (cho Tool 3)',
                'Template dự án (.rte)',
                'Shared Parameter file (.txt)',
                'Screenshot / Video quy trình thủ công'
            ]),

            sectionTitle('C. Ghi chú bổ sung'),
            normalText('Bạn có ghi chú, mong muốn, hoặc góp ý gì thêm cho developer không?'),
            ...answerBoxMultiLine(5),

            separator(),
            new Paragraph({
                alignment: AlignmentType.CENTER,
                spacing: { before: 200 },
                children: [new TextRun({
                    text: '— Cảm ơn bạn đã dành thời gian điền thông tin! —',
                    italics: true, size: 22, color: '1F4E79'
                })]
            }),
            new Paragraph({
                alignment: AlignmentType.CENTER,
                spacing: { after: 100 },
                children: [new TextRun({
                    text: 'CIC BIM Addin Development Team',
                    size: 20, color: '999999'
                })]
            })
        ]
    }]
});

// ═══════════════════════════════════════════════════════════
// Export to .docx
// ═══════════════════════════════════════════════════════════
const outputPath = 'CIC_BIM_Addin_ThongTinKyThuat_5Tools.docx';
const buffer = await Packer.toBuffer(doc);
fs.writeFileSync(outputPath, buffer);
console.log(`✅ Đã tạo file: ${outputPath}`);
console.log(`📂 Vị trí: ${process.cwd()}\\${outputPath}`);
