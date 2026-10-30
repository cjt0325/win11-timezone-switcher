# 第三方数据

离线国家、城市与 Windows/IANA 映射索引由 **Unicode CLDR 48** 的数据生成。

- 项目：https://github.com/unicode-org/cldr
- 固定提交：`acd6d88ae493633240e19a87a721076a8a75c310`
- 许可证：[Unicode License V3](licenses/UNICODE-LICENSE.txt)
- 每份输入数据的完整地址和版本：[data/sources.json](data/sources.json)
- 生成脚本：[tools/generate_search_data.py](tools/generate_search_data.py)

索引按 Windows 时区 ID 合并国家名称、城市名称和 IANA 标识，再加入程序自身的常见缩写。Unicode 数据仍适用其原许可证；项目自身代码使用 MIT 许可证。

索引另有 5 项由本项目维护的搜索标签，用于 CLDR 48 未收录的 Windows 新时区和旧版兼容项。实际时区偏移与夏令时始终读取 Windows，不由搜索索引决定。

构建使用已包含的索引，不需要下载 CLDR，也不需要 Python。仅刷新索引时需 Python 和网络。
