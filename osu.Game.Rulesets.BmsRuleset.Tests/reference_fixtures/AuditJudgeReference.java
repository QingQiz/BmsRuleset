import bms.player.beatoraja.play.JudgeProperty;
public class AuditJudgeReference {
  public static void main(String[] args) {
    for (var property : new JudgeProperty[]{JudgeProperty.FIVEKEYS, JudgeProperty.SEVENKEYS, JudgeProperty.PMS}) {
      for (int rank=0; rank<5; rank++) {
        for (var type : JudgeProperty.NoteType.values()) {
          if (property==JudgeProperty.PMS && (type==JudgeProperty.NoteType.SCRATCH || type==JudgeProperty.NoteType.LONGSCRATCH_END)) continue;
          var rows=property.getJudgeWindow(type, property.windowrule.judgerank[rank][1], new int[]{100,100,100});
          for(int i=0;i<rows.length/2;i++)
            System.out.println(property+","+type+","+rank+","+i+","+rows[i*2]+","+rows[i*2+1]);
        }
      }
    }
  }
}
