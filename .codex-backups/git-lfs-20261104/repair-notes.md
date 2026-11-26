# 本地 LFS 上传修复（2026-11-04）

用户截图：UGit 5.54.4上传失败，batch response: More than 100 objects specified。

核实：UGit内置Git 2.45.2 / Git LFS 3.5.1，系统Git 2.49.0 / Git LFS 3.6.1。内置版本的本地帮助没有lfs.transfer.batchSize，系统版本有。原仓库已经设batchSize=50，但上传钩子使用PATH内的git lfs，UGit可选中内置版本。该差异能解释为什么仓库的批次限制没有被内置LFS采用；服务器实际请求数量未抓包，未声称已完成服务器验证。

本地修改：

- lfs.transfer.batchSize：保留50。
- lfs.concurrenttransfers：32 → 8。
- lfs.dialtimeout：仓库原未设（继承全局3）→ 仓库30秒。
- lfs.tlstimeout：仓库原未设（继承全局3）→ 仓库30秒。
- lfs.activitytimeout：仓库原未设 → 120秒。
- .git/hooks/pre-push：只将执行LFS上传的调用改为"C:/Program Files/Git/cmd/git-lfs.exe" pre-push "$@"，强制使用支持批次配置的系统版。保留原LFS存在检查，换行统一LF。原hook保存在pre-push.original。

本地验证通过：sh -n语法检查；UGit内置Git可读取新仓库配置；目标系统LFS为3.6.1。

没有修改全局Git配置、UGit安装包、远端URL、工作代码、提交或历史。HEAD仍为5ee2b556955161095f24032c1ffa024c51a9570f，origin/master本地记录仍为d0f66ebcc81e946e5499751b245e9d9162f0a5b6，领先1次提交。

实际推送曾被自动审批阻止，未启动；用户随后明确选择“先不上传，只修复本地配置”，因此没有重试任何上传。远端与LFS服务器是否已恢复须留待用户下一次上传确认。未来若UGit重新安装默认LFS hook，需在设置里改用系统Git/LFS，或重新应用本地hook指定。

还原：把pre-push.original内容复制回.git/hooks/pre-push（保持LF）；将仓库concurrenttransfers设回32；移除仓库dialtimeout、tlstimeout、activitytimeout配置。batchSize原本就是50，不需移除。
