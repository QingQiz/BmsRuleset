import bms.player.beatoraja.play.JudgeProperty;
public class DefExReference {
 public static void main(String[] args) {
  for (JudgeProperty p : new JudgeProperty[]{JudgeProperty.FIVEKEYS,JudgeProperty.SEVENKEYS,JudgeProperty.PMS})
   for (int ex : new int[]{-1,0,75,99,101,133}) {
    int normal = p==JudgeProperty.PMS?70:75;
    int rank=ex>0?ex*normal/100:normal;
    long[] window=p.getJudgeWindow(JudgeProperty.NoteType.NOTE,rank,new int[]{100,100,100});
    System.out.println(p+","+ex+","+rank+","+window[0]+","+window[1]);
   }
 }
}
