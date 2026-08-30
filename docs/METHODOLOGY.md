# Quy trình thẩm định máy đã qua sử dụng

Tài liệu này mô tả cách nghĩ đứng sau phần mềm. Đọc nó trước khi đọc code sẽ giúp bạn
hiểu vì sao từng phép kiểm tra tồn tại, và quan trọng hơn — hiểu giới hạn của chúng.

---

## Nguyên tắc nền tảng

**Không tin bất kỳ con số đơn lẻ nào.**

Người bán có thể cài lại Windows, thay pin, lau sạch vỏ, thậm chí ghi lại serial trong BIOS.
Nhưng để làm cho *tất cả* các dấu vết cùng khớp với một câu chuyện giả thì cần công sức
vượt xa giá trị của việc lừa một giao dịch. Vì vậy phương pháp ở đây không đi tìm
"con số xấu" — nó đi tìm **chỗ các con số mâu thuẫn với nhau**.

Mỗi linh kiện trong máy mang một loại đồng hồ riêng, và các đồng hồ này độc lập với nhau:

| Nguồn | Đồng hồ nó mang | Xoá được không |
|---|---|---|
| Ổ cứng | Số giờ cấp điện, số lần bật/tắt, tổng dữ liệu đã ghi | Không, nằm trong firmware ổ |
| Pin | Số chu kỳ sạc, dung lượng còn lại, ngày sản xuất | Không, nằm trong vi điều khiển pin |
| Tấm nền | Tuần/năm sản xuất trong EDID | Không, trừ khi thay tấm nền |
| BIOS | Ngày phát hành, phiên bản | Có, nhưng cần thiết bị chuyên dụng |
| Windows | Ngày cài, nhật ký sự cố | Có, chỉ cần cài lại |
| Vỏ máy | Ốc toét, xước, bản lề | Không, chỉ nhìn được bằng mắt |

Khi bốn đồng hồ đầu tiên kể cùng một câu chuyện, độ tin cậy rất cao.
Khi chúng lệch nhau, điểm lệch chính là câu hỏi cần đặt cho người bán.

---

## Bảy tầng kiểm tra, theo thứ tự ưu tiên

### Tầng 1 — Định danh: máy này có đúng là máy nó nói không?

Đọc SMBIOS thô từ firmware (không qua WMI, vì WMI đã làm sạch dữ liệu) và kiểm tra:

- Serial hệ thống, serial bo mạch, serial vỏ máy có được nạp đầy đủ không.
  Chuỗi kiểu `To Be Filled By O.E.M.` hoặc `Default string` trên máy thương hiệu
  là dấu hiệu bo mạch không phải hàng gốc, hoặc BIOS đã bị ghi lại bằng programmer.
- Bảng ACPI **MSDM**: khoá Windows OEM nạp tại nhà máy, gắn với bo mạch.
  Laptop thương hiệu bán kèm Windows mà không có MSDM → bo mạch đã bị thay,
  hoặc máy vốn bán ra không kèm Windows. Đây là một trong những chỉ dấu mạnh nhất.
- Đối chiếu với tem serial in trên vỏ máy. Đây là bước bắt buộc làm bằng tay.

### Tầng 2 — Firmware: có ai đó đã can thiệp vào tầng dưới hệ điều hành chưa?

Đây là tầng quan trọng nhất vì mã độc ở đây **sống sót qua cả format ổ cứng lẫn thay ổ mới**.
Bốn hướng kiểm tra:

**a. Chuỗi khởi động Secure Boot.** Muốn nạp bootloader không có chữ ký hợp lệ,
kẻ tấn công gần như buộc phải xoá khoá nền tảng (PK) hoặc tắt Secure Boot.
Trạng thái *Setup Mode* — nghĩa là PK rỗng — là cảnh báo nghiêm trọng nhất
mà công cụ này phát ra.

**b. Bảng WPBT.** Đây là cơ chế cho phép firmware nhúng một file `.exe`
mà Windows chạy với quyền hệ thống ở **mỗi lần khởi động**, độc lập với ổ cứng.
Một số OEM dùng WPBT hợp pháp cho driver và phần mềm chống trộm.
Nhưng đây cũng chính là đường đi của LoJax và các bootkit thương mại.
Sự có mặt của WPBT không tự nó chứng minh điều xấu — nó là câu hỏi cần trả lời:
*hãng này có công bố dùng WPBT cho dòng máy này không?*

**c. Cấu hình khởi động Windows.** Ba công tắc mà máy dùng bình thường không cần bật:
`testsigning`, `nointegritychecks`, `debug`. Mỗi cái đều nới lỏng việc xác thực
chữ ký driver kernel.

**d. Nội dung phân vùng EFI.** Mọi file `.efi` nằm ngoài đường dẫn của Microsoft
và các OEM lớn đều cần được giải thích. Lưu ý: bản cài Linux, rEFInd hay công cụ
chẩn đoán của hãng cũng tạo file ở đây, nên đây là tín hiệu cần đối chiếu
chứ không phải kết luận.

### Tầng 3 — Ổ lưu trữ: cuốn nhật ký trung thực nhất

S.M.A.R.T. cho biết những thứ mà không ai xoá được bằng cách format:

- **Số giờ cấp điện.** Trên 20.000 giờ là máy dùng cường độ cao;
  trên 35.000 giờ thì bất kể S.M.A.R.T. còn "xanh", rủi ro hỏng đột ngột đã rất lớn.
- **Sector đã tráo / đang chờ tráo / không sửa được.** Bất kỳ con số nào lớn hơn 0
  ở nhóm này đều đáng lo. *Pending sectors* nguy hiểm nhất vì dữ liệu trên đó
  có thể mất bất cứ lúc nào.
- **Tuổi thọ NAND còn lại và tổng TB đã ghi.** Máy đã ghi 200 TB không phải máy văn phòng —
  đó là máy từng chạy máy ảo, dựng phim, hoặc làm node blockchain.
- **Cảm biến va đập (attribute 0xBF).** Con số cao trên ổ cơ nghĩa là máy từng bị rơi.
  Đây là lúc cần soi kỹ bản lề và khung vỏ.
- **Lỗi CRC (0xC7).** Lỗi này nằm ở *cáp và cổng*, không phải ở ổ.
  Phân biệt đúng chỗ giúp bạn không trả tiền oan cho một cái ổ vẫn còn tốt.

### Tầng 4 — Pin: đồng hồ đo tuổi thật của laptop

Số chu kỳ sạc và độ chai pin nằm trong vi điều khiển của chính viên pin.
Một máy có vỏ đẹp như mới nhưng pin đã qua 900 chu kỳ là một máy đã được dùng
rất nhiều, chỉ là được giữ gìn cẩn thận.

Ngược lại — và đây là phép đối chiếu quan trọng — **pin gần như mới trên máy có
ổ cứng chạy 20.000 giờ** nghĩa là pin vừa được thay trước khi bán.
Điều này không xấu, nhưng nó nói với bạn rằng tuổi thật của máy nằm ở con số
giờ chạy ổ cứng, không phải ở tình trạng pin.

### Tầng 5 — Nhiệt: chỉ lộ ra khi máy nóng thật

Đo nhiệt độ lúc máy nghỉ gần như vô nghĩa. Toàn bộ giá trị của bài kiểm tra nhiệt
nằm ở việc chạy tải nặng 15 phút rồi quan sát:

- **Nhiệt độ tối đa.** Chạm ngưỡng cắt nhiệt nghĩa là CPU đang tự hạ xung để tự bảo vệ —
  hiệu năng thực tế thấp hơn thông số đáng kể.
- **Nhiệt độ thấp nhất.** Nhiệt độ *nghỉ* cao là dấu hiệu rõ hơn cả nhiệt độ tải:
  nó nói rằng luồng gió bị chặn hoặc keo tản nhiệt đã mất tác dụng.
- **Chênh lệch nhân ↔ điểm nóng của GPU.** Trên card khoẻ, chênh lệch này dưới 15 °C.
  Trên 25 °C là dấu hiệu kinh điển của keo tản nhiệt đã tách lớp —
  thường gặp ở card từng chạy tải nặng liên tục nhiều tháng.
- **Xung nhịp đạt được so với thiết kế.** Chỉ đạt 70% xung thiết kế nghĩa là
  có thứ gì đó đang giới hạn: nhiệt, công suất, hoặc BIOS.

### Tầng 6 — Màn hình: EDID trả lời câu hỏi, mắt trả lời phần còn lại

EDID chứa mã tấm nền thật và tuần/năm sản xuất. Tấm nền sản xuất muộn hơn
ngày phát hành BIOS hai năm trở lên nghĩa là **màn hình đã được thay**.
Việc thay màn tự nó không xấu, nhưng nó cho biết máy từng bị va đập hoặc hỏng.

Phần còn lại — điểm chết, điểm sáng, hở sáng, ám màu, lưu ảnh — không có phần mềm nào
phát hiện được. Bắt buộc phải nhìn bằng mắt, trong phòng tối, với các nền đơn sắc.
Đó là lý do công cụ này có sẵn bài kiểm tra toàn màn hình.

### Tầng 7 — Nhật ký sự cố: những gì Windows đã âm thầm ghi lại

Trong 90 ngày gần nhất:

- **WHEA** ghi lỗi ở mức CPU, RAM và bus PCIe. Số lượng lớn thường đi kèm RAM lỗi
  hoặc khe PCIe tiếp xúc kém.
- **Disk event 7 / 51 / 153** báo lỗi truy cập ổ — thường xuất hiện *sớm hơn* cả S.M.A.R.T.
- **BugCheck** là màn hình xanh. Ba lần trở lên trong 90 ngày là chuyện cần giải thích.

Lưu ý quan trọng: **Windows vừa cài lại thì nhật ký sạch trơn.**
Nếu ngày cài Windows chỉ cách đây vài ngày, đừng coi Event Log sạch là bằng chứng tốt —
hãy dựa hoàn toàn vào số liệu phần cứng vốn không bị xoá.

---

## Cách đọc kết quả

Công cụ chia phát hiện theo **mức độ chắc chắn**, không chỉ theo mức nghiêm trọng:

- **Bằng chứng cứng** (độ tin cậy ≥ 0.9): S.M.A.R.T. báo sector hỏng, pin chai 45%,
  Setup Mode đang bật. Những thứ này đọc trực tiếp từ phần cứng, không suy diễn.
- **Suy đoán có cơ sở** (0.5 – 0.9): WPBT tồn tại, tấm nền lệch tuổi so với BIOS,
  OEM ID không khớp. Cần đối chiếu thêm trước khi kết luận.
- **Tín hiệu yếu** (< 0.5): dùng để gợi ý hướng kiểm tra, không dùng để kết tội.

Điểm trừ được nhân với độ tin cậy, nên một suy đoán yếu không kéo tụt điểm
như một bằng chứng cứng.

**Kết luận "không phát hiện can thiệp" chỉ có giá trị khi chạy với quyền Administrator.**
Không có quyền đó, phần mềm bỏ qua kiểm tra phân vùng EFI, cấu hình khởi động và
phần lớn biến UEFI — và trạng thái tốt nhất nó dám đưa ra là *chưa đủ dữ liệu*,
chứ không phải *sạch*.

---

## Những gì công cụ này *không* làm được

Trung thực về giới hạn quan trọng ngang với việc phát hiện vấn đề:

1. **Không đọc được nội dung chip BIOS.** Muốn so sánh byte-by-byte với bản gốc của hãng
   cần tháo máy và dùng programmer SPI (CH341A hoặc tương đương). Phần mềm chỉ đọc được
   những gì firmware *khai báo* — mà một firmware đã bị can thiệp hoàn toàn có thể nói dối.
2. **Không phát hiện được RAM lỗi nhẹ.** Phải chạy MemTest86 từ USB, ngoài Windows.
3. **Không thấy artifact của GPU.** Điểm ảnh lỗi, sọc, chấm màu khi card yếu chỉ hiện
   khi chạy tải đồ hoạ thật và nhìn bằng mắt.
4. **Không đánh giá được tình trạng vật lý.** Tụ phồng, bản lề nứt, ốc toét, dấu ẩm mốc —
   tất cả đều phải mở máy ra nhìn.
5. **Không kiểm tra được nguồn (PSU).** Với PC bàn, đây thường là linh kiện nguy hiểm nhất
   và cũng là thứ người bán ít nói tới nhất.

Vì vậy mọi báo cáo đều kết thúc bằng một checklist kiểm tra tay, và checklist đó
thay đổi theo những gì vừa phát hiện được trên máy.
